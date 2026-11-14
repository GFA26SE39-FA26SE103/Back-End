using System.Data.Common;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Supermarket.Application;
using Supermarket.Infrastructure.Persistence.Scaffolded;

namespace Supermarket.Infrastructure.Persistence;

/// <summary>Paged, read-only queries over dbo.AuditLog (newest first, server-side filtering and paging).</summary>
public sealed class EfAuditLogQueries(AppDbContext db) : IAuditLogQueries
{
    internal static IQueryable<AuditLog> Filter(IQueryable<AuditLog> rows, AuditLogWindow window)
    {
        rows = rows.Where(row => row.CreatedAt <= window.GeneratedAt);
        if (window.FromUtc is { } from) rows = rows.Where(row => row.CreatedAt >= from);
        if (window.ToUtcExclusive is { } to) rows = rows.Where(row => row.CreatedAt < to);
        if (window.UserId is { } userId) rows = rows.Where(row => row.UserId == userId);
        if (window.Action is { } action) rows = rows.Where(row => row.Action == action);
        if (window.EntityType is { } entityType) rows = rows.Where(row => row.EntityType == entityType);
        if (window.EntityId is { } entityId) rows = rows.Where(row => row.EntityId == entityId);
        if (window.Result is { } result) rows = rows.Where(row => row.ResultStatus == result);
        if (window.Search is { } search)
            rows = rows.Where(row => row.Action.Contains(search) || row.EntityType.Contains(search)
                || (row.Reason != null && row.Reason.Contains(search))
                || (row.User != null && (row.User.FullName.Contains(search) || row.User.Email.Contains(search))));
        return rows;
    }

    private static readonly Expression<Func<AuditLog, AuditLogView>> View = row => new(
        row.AuditLogId, row.CreatedAt, row.UserId,
        row.User == null ? null : row.User.FullName, row.User == null ? null : row.User.Email, row.User == null ? null : row.User.Role.Name,
        row.Action, row.EntityType, row.EntityId, row.Reason, row.ResultStatus, row.IpAddress, row.OldValuesJson, row.NewValuesJson);

    internal IQueryable<AuditLogView> Page(AuditLogWindow window) => Filter(db.AuditLogs.AsNoTracking(), window)
        .OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.AuditLogId)
        .Skip((window.Page - 1) * window.PageSize).Take(window.PageSize).Select(View);

    public async Task<AuditLogPage> List(AuditLogWindow window, string timeZone, CancellationToken ct)
    {
        try
        {
            var count = await Filter(db.AuditLogs.AsNoTracking(), window).CountAsync(ct);
            var page = await Page(window).ToArrayAsync(ct);
            return new(page.Select(Utc).ToArray(), count, window.Page, window.PageSize, window.GeneratedAt, timeZone);
        }
        catch (Exception error) when (error is DbException or TimeoutException) { throw Unavailable(); }
    }

    public async Task<AuditLogView?> Get(Guid auditLogId, CancellationToken ct)
    {
        try
        {
            var row = await db.AuditLogs.AsNoTracking().Where(row => row.AuditLogId == auditLogId).Select(View).SingleOrDefaultAsync(ct);
            return row is null ? null : Utc(row);
        }
        catch (Exception error) when (error is DbException or TimeoutException) { throw Unavailable(); }
    }

    public async Task<AuditLogFacets> Facets(CancellationToken ct)
    {
        try
        {
            var actions = await db.AuditLogs.AsNoTracking().Select(row => row.Action).Distinct().OrderBy(value => value).ToArrayAsync(ct);
            var types = await db.AuditLogs.AsNoTracking().Select(row => row.EntityType).Distinct().OrderBy(value => value).ToArrayAsync(ct);
            var results = await db.AuditLogs.AsNoTracking().Where(row => row.ResultStatus != null).Select(row => row.ResultStatus!)
                .Distinct().OrderBy(value => value).ToArrayAsync(ct);
            var actorIds = db.AuditLogs.AsNoTracking().Where(row => row.UserId != null).Select(row => row.UserId!.Value).Distinct();
            var actors = await db.UserAccounts.AsNoTracking().Where(account => actorIds.Contains(account.UserId))
                .OrderBy(account => account.FullName).Select(account => new AuditLogActor(account.UserId, account.FullName, account.Role.Name)).ToArrayAsync(ct);
            return new(actions, types, results, actors);
        }
        catch (Exception error) when (error is DbException or TimeoutException) { throw Unavailable(); }
    }

    private static AuditLogView Utc(AuditLogView row) => row with { CreatedAt = DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc) };

    private static Supermarket.Application.ApplicationException Unavailable()
        => new("AUDIT_DATA_UNAVAILABLE", "Audit log data is temporarily unavailable. Retry later.", 503);
}
