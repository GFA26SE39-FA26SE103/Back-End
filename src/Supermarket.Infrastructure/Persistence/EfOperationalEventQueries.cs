using System.Data.Common;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Supermarket.Application;
using Supermarket.Infrastructure.Persistence.Scaffolded;

namespace Supermarket.Infrastructure.Persistence;

public sealed class EfOperationalEventQueries(AppDbContext db) : IOperationalEventQueries
{
    internal static IQueryable<OperationalEvent> Filter(IQueryable<OperationalEvent> rows, OperationalEventWindow window)
    {
        rows = rows.Where(row => row.DetectedAt <= window.GeneratedAt);
        if (window.Status != "ALL") rows = rows.Where(row => row.Status == window.Status);
        if (window.Priority != "ALL") rows = rows.Where(row => row.Priority == window.Priority);
        if (window.ZoneId is { } zone) rows = rows.Where(row => row.ZoneId == zone);
        if (window.FromUtc is { } from) rows = rows.Where(row => row.DetectedAt >= from);
        if (window.ToUtcExclusive is { } to) rows = rows.Where(row => row.DetectedAt < to);
        return rows;
    }

    private static readonly Expression<Func<OperationalEvent, OperationalEventView>> View = row => new(
        row.EventId, row.EventTypeId, row.EventType.Code, row.EventType.Name, row.ZoneId, row.Zone.Name,
        row.CameraId, row.Camera.Code, row.Camera.Name, row.Priority, row.Status, row.MetricValue, row.EventType.MeasurementType,
        row.DetectedAt, row.LastObservedAt, row.ClearedAt, row.UpdatedAt, row.Tasks.Count);

    internal IQueryable<OperationalEventView> Page(OperationalEventWindow window) => Filter(db.OperationalEvents.AsNoTracking(), window)
        .OrderByDescending(row => row.DetectedAt).ThenByDescending(row => row.EventId)
        .Skip((window.Page - 1) * window.PageSize).Take(window.PageSize).Select(View);

    public async Task<OperationalEventPage> List(OperationalEventWindow window, string timeZone, CancellationToken ct)
    {
        try
        {
            var count = await Filter(db.OperationalEvents.AsNoTracking(), window).CountAsync(ct);
            var rows = await Page(window).ToArrayAsync(ct);
            return new(rows.Select(Utc).ToArray(), count, window.Page, window.PageSize, window.GeneratedAt, timeZone);
        }
        catch (Exception error) when (error is DbException or TimeoutException) { throw Unavailable(); }
    }

    public async Task<OperationalEventDetail?> Get(Guid eventId, CancellationToken ct)
    {
        try
        {
            var row = await db.OperationalEvents.AsNoTracking().Where(row => row.EventId == eventId).Select(View).SingleOrDefaultAsync(ct);
            if (row is null) return null;
            var tasks = await db.Tasks.AsNoTracking().Where(task => task.EventId == eventId)
                .OrderByDescending(task => task.CreatedAt).ThenByDescending(task => task.TaskId).Take(101)
                .Select(task => new EventTaskView(task.TaskId, task.Title, task.Status, task.Priority,
                    task.CurrentAssigneeUser == null ? null : task.CurrentAssigneeUser.FullName, task.CreatedAt)).ToArrayAsync(ct);
            return new(Utc(row), tasks.Take(100).Select(task => task with { CreatedAt = DateTime.SpecifyKind(task.CreatedAt, DateTimeKind.Utc) }).ToArray(), tasks.Length > 100);
        }
        catch (Exception error) when (error is DbException or TimeoutException) { throw Unavailable(); }
    }

    private static OperationalEventView Utc(OperationalEventView row) => row with
    {
        DetectedAt = DateTime.SpecifyKind(row.DetectedAt, DateTimeKind.Utc), LastObservedAt = DateTime.SpecifyKind(row.LastObservedAt, DateTimeKind.Utc),
        UpdatedAt = DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc), ClearedAt = row.ClearedAt is { } cleared ? DateTime.SpecifyKind(cleared, DateTimeKind.Utc) : null
    };
    private static Supermarket.Application.ApplicationException Unavailable()
        => new("EVENT_DATA_UNAVAILABLE", "Operational event data is temporarily unavailable. Retry later.", 503);
}
