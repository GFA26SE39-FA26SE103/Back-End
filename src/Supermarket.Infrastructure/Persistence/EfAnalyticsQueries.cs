using System.Data.Common;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Supermarket.Application;
using Supermarket.Infrastructure.Persistence.Scaffolded;

namespace Supermarket.Infrastructure.Persistence;

public sealed class EfAnalyticsQueries(AppDbContext db) : IAnalyticsQueries
{
    public async Task<List<AnalyticsBucket>> Read(AnalyticsWindow window, CancellationToken ct)
    {
        try
        {
            // Sequential queries: one scoped DbContext cannot execute concurrent commands.
            var rows = await Events(window).ToListAsync(ct);
            rows.AddRange(await Incidents(window).ToListAsync(ct));
            rows.AddRange(await Tasks(window).ToListAsync(ct));
            return rows;
        }
        catch (Exception error) when (error is DbException or TimeoutException)
        {
            throw new Supermarket.Application.ApplicationException("ANALYTICS_UNAVAILABLE", "Analytics data is temporarily unavailable. Retry later.", 503);
        }
    }

    // Group by local calendar day using explicit UTC boundaries. This also supports DST
    // without assuming every local day lasts 24 hours or relying on SQL time-zone IDs.
    private sealed class Dated<T>
    {
        public T Row { get; set; } = default!;
        public int Day { get; set; }
    }
    private static IQueryable<Dated<T>> Days<T>(IQueryable<T> source, Expression<Func<T, DateTime>> timestamp, AnalyticsWindow window)
    {
        // Balance the CASE tree: SQL Server allows at most ten nested CASE levels.
        Expression Day(int first, int last)
        {
            if (first == last) return Expression.Constant(first);
            var middle = (first + last + 1) / 2;
            return Expression.Condition(Expression.GreaterThanOrEqual(timestamp.Body, Expression.Constant(window.DayStartsUtc[middle])),
                Day(middle, last), Day(first, middle - 1));
        }
        var projection = Expression.MemberInit(Expression.New(typeof(Dated<T>)),
            Expression.Bind(typeof(Dated<T>).GetProperty(nameof(Dated<T>.Row))!, timestamp.Parameters[0]),
            Expression.Bind(typeof(Dated<T>).GetProperty(nameof(Dated<T>.Day))!, Day(0, window.DayStartsUtc.Length - 1)));
        return source.Select(Expression.Lambda<Func<T, Dated<T>>>(projection, timestamp.Parameters));
    }

    internal IQueryable<AnalyticsBucket> Events(AnalyticsWindow w)
        => Days(db.OperationalEvents.AsNoTracking().Where(row => row.DetectedAt >= w.FromUtc && row.DetectedAt < w.ToUtcExclusive
                && row.DetectedAt <= w.GeneratedAt && (w.ZoneId == null || row.ZoneId == w.ZoneId)), row => row.DetectedAt, w)
            .GroupBy(item => new { item.Day, item.Row.ZoneId, item.Row.Status, item.Row.Priority })
            .Select(group => new AnalyticsBucket("EVENT", group.Key.Day, group.Key.ZoneId, group.Key.Status, group.Key.Priority, group.Count(), 0, 0,
                group.Count(item => item.Row.Status == "CLEARED" && item.Row.ClearedAt >= item.Row.DetectedAt && item.Row.ClearedAt <= w.GeneratedAt),
                group.Sum(item => item.Row.Status == "CLEARED" && item.Row.ClearedAt >= item.Row.DetectedAt && item.Row.ClearedAt <= w.GeneratedAt
                    ? (double)(EF.Functions.DateDiffSecond(item.Row.DetectedAt, item.Row.ClearedAt) ?? 0) : 0), "EXCLUDED", "EXCLUDED"));

    internal IQueryable<AnalyticsBucket> Incidents(AnalyticsWindow w)
        => Days(db.Incidents.AsNoTracking().Where(row => row.CreatedAt >= w.FromUtc && row.CreatedAt < w.ToUtcExclusive
                && row.CreatedAt <= w.GeneratedAt && (w.ZoneId == null || row.ZoneId == w.ZoneId)), row => row.CreatedAt, w)
            .GroupBy(item => new { item.Day, item.Row.ZoneId, item.Row.Status })
            .Select(group => new AnalyticsBucket("INCIDENT", group.Key.Day, group.Key.ZoneId, group.Key.Status, null, group.Count(), 0, 0,
                group.Count(item => item.Row.Status == "CLOSED" && item.Row.ClosedAt >= item.Row.CreatedAt && item.Row.ClosedAt <= w.GeneratedAt),
                group.Sum(item => item.Row.Status == "CLOSED" && item.Row.ClosedAt >= item.Row.CreatedAt && item.Row.ClosedAt <= w.GeneratedAt
                    ? (double)(EF.Functions.DateDiffSecond(item.Row.CreatedAt, item.Row.ClosedAt) ?? 0) : 0), "EXCLUDED", "EXCLUDED"));

    internal IQueryable<AnalyticsBucket> Tasks(AnalyticsWindow w)
        => Days(db.Tasks.AsNoTracking().Where(row => row.CreatedAt >= w.FromUtc && row.CreatedAt < w.ToUtcExclusive
                && row.CreatedAt <= w.GeneratedAt && (w.ZoneId == null || row.ZoneId == w.ZoneId)), row => row.CreatedAt, w)
            .GroupBy(item => new
            {
                item.Day, item.Row.ZoneId, item.Row.Status,
                ResponseSla = item.Row.Status == "CANCELLED" ? "EXCLUDED"
                    : item.Row.ResponseDeadlineAt == null || item.Row.ResponseDeadlineAt < item.Row.CreatedAt
                        || (item.Row.AcceptedAt == null && item.Row.Status != "OFFERED")
                        || item.Row.AcceptedAt < item.Row.CreatedAt || item.Row.AcceptedAt > w.GeneratedAt ? "UNAVAILABLE"
                    : item.Row.AcceptedAt != null ? (item.Row.AcceptedAt <= item.Row.ResponseDeadlineAt ? "MET" : "BREACHED")
                    : item.Row.ResponseDeadlineAt < w.GeneratedAt ? "BREACHED" : "PENDING",
                ResolutionSla = item.Row.Status == "CANCELLED" ? "EXCLUDED"
                    : item.Row.ResolutionDeadlineAt == null || item.Row.ResolutionDeadlineAt < item.Row.CreatedAt ? "UNAVAILABLE"
                    : item.Row.Status == "VERIFIED" ? (item.Row.VerifiedAt == null || item.Row.VerifiedAt < item.Row.CreatedAt || item.Row.VerifiedAt > w.GeneratedAt ? "UNAVAILABLE"
                        : item.Row.VerifiedAt <= item.Row.ResolutionDeadlineAt ? "MET" : "BREACHED")
                    : item.Row.ResolutionDeadlineAt < w.GeneratedAt ? "BREACHED" : "PENDING"
            })
            .Select(group => new AnalyticsBucket("TASK", group.Key.Day, group.Key.ZoneId, group.Key.Status, null, group.Count(),
                group.Count(item => item.Row.Status != "CANCELLED" && item.Row.AcceptedAt >= item.Row.CreatedAt && item.Row.AcceptedAt <= w.GeneratedAt),
                group.Sum(item => item.Row.Status != "CANCELLED" && item.Row.AcceptedAt >= item.Row.CreatedAt && item.Row.AcceptedAt <= w.GeneratedAt
                    ? (double)(EF.Functions.DateDiffSecond(item.Row.CreatedAt, item.Row.AcceptedAt) ?? 0) : 0),
                group.Count(item => item.Row.Status == "VERIFIED" && item.Row.VerifiedAt >= item.Row.CreatedAt && item.Row.VerifiedAt <= w.GeneratedAt),
                group.Sum(item => item.Row.Status == "VERIFIED" && item.Row.VerifiedAt >= item.Row.CreatedAt && item.Row.VerifiedAt <= w.GeneratedAt
                    ? (double)(EF.Functions.DateDiffSecond(item.Row.CreatedAt, item.Row.VerifiedAt) ?? 0) : 0), group.Key.ResponseSla, group.Key.ResolutionSla));
}
