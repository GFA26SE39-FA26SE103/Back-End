using Supermarket.Domain;

namespace Supermarket.Application;

public sealed record OperationalEventQuery(string? Status = null, string? Priority = null, Guid? ZoneId = null,
    DateOnly? From = null, DateOnly? To = null, int Page = 1, int PageSize = 50);
public sealed record OperationalEventWindow(string Status, string Priority, Guid? ZoneId, DateTime? FromUtc,
    DateTime? ToUtcExclusive, DateTime GeneratedAt, int Page, int PageSize);
public sealed record OperationalEventView(Guid EventId, Guid EventTypeId, string EventTypeCode, string EventTypeName,
    Guid ZoneId, string ZoneName, Guid CameraId, string CameraCode, string CameraName, string Priority, string Status,
    decimal MetricValue, string MeasurementType, DateTime DetectedAt, DateTime LastObservedAt, DateTime? ClearedAt, DateTime UpdatedAt, int TaskCount)
{
    public string SourceCode => $"EVT-{EventId.ToString("N")[..8].ToUpperInvariant()}";
}
public sealed record OperationalEventPage(OperationalEventView[] Items, int TotalCount, int Page, int PageSize,
    DateTime GeneratedAt, string TimeZone);
public sealed record EventTaskView(Guid TaskId, string Title, string Status, string Priority, string? AssigneeName, DateTime CreatedAt);
public sealed record OperationalEventDetail(OperationalEventView Event, EventTaskView[] Tasks, bool HasMoreTasks);
public sealed record EventZoneOption(Guid ZoneId, string Name, string Status);
public interface IOperationalEventQueries
{
    Task<OperationalEventPage> List(OperationalEventWindow window, string timeZone, CancellationToken ct);
    Task<OperationalEventDetail?> Get(Guid eventId, CancellationToken ct);
}

public sealed class OperationalEventHistory(IOperationalEventQueries queries, ISetupStore store, ICurrentUser user, IClock clock, StoreCalendar calendar)
{
    public async Task<OperationalEventPage> List(OperationalEventQuery query, CancellationToken ct)
    {
        UseCase.Viewer(user);
        var status = (query.Status ?? "ALL").Trim().ToUpperInvariant();
        var priority = (query.Priority ?? "ALL").Trim().ToUpperInvariant();
        var now = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        var today = calendar.Today(now);
        if (status is not ("ALL" or "ACTIVE" or "CLEARED") || priority is not ("ALL" or "WARNING" or "CRITICAL")
            || query.Page is < 1 or > 10000 || query.PageSize is < 1 or > 100 || query.ZoneId == Guid.Empty
            || query.From > (query.To ?? today) || query.From > today || query.To > today
            || query.From?.Year < 2000 || query.To?.Year < 2000)
            throw new ApplicationException("INVALID_EVENT_QUERY", "Use valid All/status/priority filters, dates from year 2000 through today, page 1–10000 and pageSize 1–100.", 400);
        if (query.ZoneId is { } zoneId) UseCase.Found(await store.Find<Zone>(zoneId, ct));
        return await queries.List(new(status, priority, query.ZoneId,
            query.From is { } from ? calendar.DayStart(from) : null,
            query.To is { } to ? calendar.DayStart(to.AddDays(1)) : null, now, query.Page, query.PageSize), calendar.TimeZone.Id, ct);
    }

    public async Task<OperationalEventDetail> Get(Guid eventId, CancellationToken ct)
    {
        UseCase.Viewer(user);
        return UseCase.Found(await queries.Get(eventId, ct));
    }

    public async Task<EventZoneOption[]> Zones(CancellationToken ct)
    {
        UseCase.Viewer(user);
        return (await store.List<Zone>(ct: ct)).OrderBy(zone => zone.Name).ThenBy(zone => zone.ZoneId)
            .Select(zone => new EventZoneOption(zone.ZoneId, zone.Name, zone.Status)).ToArray();
    }
}
