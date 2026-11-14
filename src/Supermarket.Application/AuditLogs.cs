using Supermarket.Domain;

namespace Supermarket.Application;

/// <summary>FE-12 §3.13.3: filters for the Admin audit-log inspector. Dates are store-local days (inclusive).</summary>
public sealed record AuditLogQuery(DateOnly? From = null, DateOnly? To = null, Guid? UserId = null, string? Action = null,
    string? EntityType = null, Guid? EntityId = null, string? Result = null, string? Search = null, int Page = 1, int PageSize = 50);
public sealed record AuditLogWindow(DateTime? FromUtc, DateTime? ToUtcExclusive, Guid? UserId, string? Action, string? EntityType,
    Guid? EntityId, string? Result, string? Search, DateTime GeneratedAt, int Page, int PageSize);
public sealed record AuditLogView(Guid AuditLogId, DateTime CreatedAt, Guid? UserId, string? UserName, string? UserEmail, string? UserRole,
    string Action, string EntityType, Guid? EntityId, string? Reason, string? ResultStatus, string? IpAddress,
    string? OldValuesJson, string? NewValuesJson);
public sealed record AuditLogPage(AuditLogView[] Items, int TotalCount, int Page, int PageSize, DateTime GeneratedAt, string TimeZone);
public sealed record AuditLogActor(Guid UserId, string FullName, string? Role);
/// <summary>Values that actually occur in the log, for filter drop-downs; never a hard-coded catalog.</summary>
public sealed record AuditLogFacets(string[] Actions, string[] EntityTypes, string[] Results, AuditLogActor[] Actors);

public interface IAuditLogQueries
{
    Task<AuditLogPage> List(AuditLogWindow window, string timeZone, CancellationToken ct);
    Task<AuditLogView?> Get(Guid auditLogId, CancellationToken ct);
    Task<AuditLogFacets> Facets(CancellationToken ct);
}

/// <summary>Read-only Admin inspection of dbo.AuditLog. The log is append-only: this use case never edits or deletes rows.</summary>
public sealed class AuditLogs(IAuditLogQueries queries, ICurrentUser user, IClock clock, StoreCalendar calendar)
{
    public const int MaxPageSize = 100;

    public Task<AuditLogPage> List(AuditLogQuery query, CancellationToken ct)
    {
        UseCase.Admin(user);
        var now = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        var today = calendar.Today(now);
        if (query.Page is < 1 or > 10000 || query.PageSize is < 1 or > MaxPageSize || query.UserId == Guid.Empty || query.EntityId == Guid.Empty
            || query.From > (query.To ?? today) || query.From > today || query.To > today || query.From?.Year < 2000 || query.To?.Year < 2000)
            throw new ApplicationException("INVALID_AUDIT_QUERY",
                $"Use dates from year 2000 through today (from ≤ to), page 1–10000 and pageSize 1–{MaxPageSize}.", 400);
        return queries.List(new(
            query.From is { } from ? calendar.DayStart(from) : null,
            query.To is { } to ? calendar.DayStart(to.AddDays(1)) : null,
            query.UserId, Code(query.Action, "Action"), Text(query.EntityType, "Entity type"), query.EntityId,
            Code(query.Result, "Result"), Text(query.Search, "Search"), now, query.Page, query.PageSize), calendar.TimeZone.Id, ct);
    }

    public async Task<AuditLogView> Get(Guid auditLogId, CancellationToken ct)
    {
        UseCase.Admin(user);
        return UseCase.Found(await queries.Get(auditLogId, ct));
    }

    public Task<AuditLogFacets> Facets(CancellationToken ct)
    {
        UseCase.Admin(user);
        return queries.Facets(ct);
    }

    private static string? Text(string? value, string field)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        Rules.Optional(text, 100, field);
        return text;
    }

    private static string? Code(string? value, string field) => Text(value, field)?.ToUpperInvariant();
}
