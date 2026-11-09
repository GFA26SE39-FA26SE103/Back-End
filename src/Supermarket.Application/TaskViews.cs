using System.Text.Json;
using Supermarket.Domain;
namespace Supermarket.Application;

/// <summary>Builds offer/task views with Zone, camera and OperationalEvent context.</summary>
public sealed class TaskViews(ISetupStore store, TaskDispatch dispatch)
{
    public async Task<StaffTaskView[]> Tasks(List<WorkTask> tasks, CancellationToken ct)
    {
        if (tasks.Count == 0) return [];
        var context = await Load(tasks, ct);
        var taskIds = tasks.Select(t => t.TaskId).ToList();
        var assignments = await store.List<TaskAssignmentHistory>(a => taskIds.Contains(a.TaskId), ct);
        var taskOffers = await store.List<TaskOffer>(o => taskIds.Contains(o.TaskId), ct);
        var assigneeIds = tasks.Where(t => t.CurrentAssigneeUserId != null).Select(t => t.CurrentAssigneeUserId!.Value).Distinct().ToList();
        var names = (await store.List<UserAccount>(u => assigneeIds.Contains(u.UserId), ct)).ToDictionary(u => u.UserId, u => u.FullName);
        return tasks.Select(t =>
        {
            var ev = t.EventId is { } eventId ? context.Events.GetValueOrDefault(eventId) : null;
            var current = assignments.Where(a => a.TaskId == t.TaskId && a.StaffUserId == t.CurrentAssigneeUserId && a.Status == OfferStatus.Accepted && a.EndedAt == null)
                .OrderByDescending(a => a.AssignedAt).FirstOrDefault();
            var needsAssignment = t.Status == WorkTaskStatus.Offered && t.CurrentAssigneeUserId is null
                && !taskOffers.Any(o => o.TaskId == t.TaskId && o.Status == TaskOfferStatus.Pending);
            var firstPriority = context.FirstOfferPriority.GetValueOrDefault(t.TaskId);
            var sla = IncidentSlaPolicyService.ReadSnapshot(t.SlaSnapshotJson);
            return new StaffTaskView(t.TaskId, t.OriginType, SourceCode(t), ev is null ? null : context.EventTypes.GetValueOrDefault(ev.EventTypeId)?.Code, t.Title, t.Description, t.Priority,
                firstPriority is not null && TaskRules.Rank(firstPriority) < TaskRules.Rank(t.Priority) ? firstPriority : null,
                t.ZoneId, context.Zones.GetValueOrDefault(t.ZoneId)?.Name ?? "", ev is null ? null : context.Cameras.GetValueOrDefault(ev.CameraId)?.Code, t.Status,
                current?.AssignmentType ?? TaskAssignmentType.Offer, t.CurrentAssigneeUserId, t.CurrentAssigneeUserId is { } id ? names.GetValueOrDefault(id) : null,
                t.CreatedAt, t.AssignedAt, t.AcceptedAt, t.PlannedStartAt, t.StartedAt, t.SubmittedAt, ev?.ClearedAt, needsAssignment, t.UpdatedAt,
                t.SlaPolicyId, sla?.PriorityCode, sla?.PolicyVersion, t.ResponseDeadlineAt ?? sla?.ResponseDeadlineAt,
                t.ResolutionDeadlineAt ?? sla?.ResolutionDeadlineAt);
        }).ToArray();
    }

    public async Task<TaskOfferView[]> Offers(List<TaskOffer> offers, CancellationToken ct)
    {
        if (offers.Count == 0) return [];
        var taskIds = offers.Select(o => o.TaskId).Distinct().ToList();
        var tasks = (await store.List<WorkTask>(t => taskIds.Contains(t.TaskId), ct)).ToDictionary(t => t.TaskId);
        var context = await Load(tasks.Values.ToList(), ct);
        var allOffers = await store.List<TaskOffer>(a => taskIds.Contains(a.TaskId), ct);
        var staffIds = offers.Select(o => o.StaffUserId).Distinct().ToList();
        var staffNames = (await store.List<UserAccount>(u => staffIds.Contains(u.UserId), ct)).ToDictionary(u => u.UserId, u => u.FullName);
        var rounds = await store.List<Notification>(n => n.TaskId != null && taskIds.Contains(n.TaskId.Value) && n.NotificationType == TaskNotifier.Offer, ct);
        var (_, maxAttempts) = await dispatch.Retry(ct);
        return offers.Where(o => tasks.ContainsKey(o.TaskId)).Select(o =>
        {
            var t = tasks[o.TaskId];
            var ev = t.EventId is { } eventId ? context.Events.GetValueOrDefault(eventId) : null;
            // Notification rows represent dispatch cycles even when a cycle found no eligible recipient.
            var manual = o.DispatchScope == TaskDispatchScope.Manual;
            var attempt = manual ? 1 : Math.Max(1, rounds.Count(n => n.TaskId == t.TaskId && n.CreatedAt <= o.OfferedAt));
            var offerMaxAttempts = manual ? 1 : maxAttempts;
            DateTime? next = o.Status == TaskOfferStatus.Pending && attempt < offerMaxAttempts ? o.ExpiresAt : null;
            var camera = ev is null ? null : context.Cameras.GetValueOrDefault(ev.CameraId);
            var (mode, unit) = Measurement(ev?.MetadataJson);
            return new TaskOfferView(o.OfferId, t.TaskId, t.OriginType, SourceCode(t), ev is null ? null : context.EventTypes.GetValueOrDefault(ev.EventTypeId)?.Code,
                t.Title, t.Description, t.Priority, t.ZoneId, context.Zones.GetValueOrDefault(t.ZoneId)?.Name ?? "", camera?.Code, camera?.Name, ev?.MetricValue, mode, unit,
                o.OfferedAt, manual ? 1 : allOffers.Where(a => a.TaskId == t.TaskId).Select(a => a.StaffUserId).Distinct().Count(),
                attempt, offerMaxAttempts, next, o.Status, o.DeclineReasonCode, ev?.ClearedAt,
                o.StaffUserId, staffNames.GetValueOrDefault(o.StaffUserId), o.DispatchCycleId, o.WaveNumber, o.DispatchScope, o.ExpiresAt, o.RespondedAt);
        }).ToArray();
    }

    private sealed record Context(Dictionary<Guid, Zone> Zones, Dictionary<Guid, OperationalEvent> Events, Dictionary<Guid, Camera> Cameras,
        Dictionary<Guid, OperationalEventType> EventTypes, Dictionary<Guid, string?> FirstOfferPriority);
    private async Task<Context> Load(List<WorkTask> tasks, CancellationToken ct)
    {
        var zoneIds = tasks.Select(t => t.ZoneId).Distinct().ToList();
        var eventIds = tasks.Where(t => t.EventId != null).Select(t => t.EventId!.Value).Distinct().ToList();
        var taskIds = tasks.Select(t => t.TaskId).ToList();
        var zones = (await store.List<Zone>(z => zoneIds.Contains(z.ZoneId), ct)).ToDictionary(z => z.ZoneId);
        var events = eventIds.Count == 0 ? [] : (await store.List<OperationalEvent>(e => eventIds.Contains(e.EventId), ct)).ToDictionary(e => e.EventId);
        var cameraIds = events.Values.Select(e => e.CameraId).Distinct().ToList();
        var cameras = cameraIds.Count == 0 ? [] : (await store.List<Camera>(c => cameraIds.Contains(c.CameraId), ct)).ToDictionary(c => c.CameraId);
        var typeIds = events.Values.Select(e => e.EventTypeId).Distinct().ToList();
        var types = typeIds.Count == 0 ? [] : (await store.List<OperationalEventType>(t => typeIds.Contains(t.EventTypeId), ct)).ToDictionary(t => t.EventTypeId);
        var firstOffers = (await store.List<Notification>(n => n.TaskId != null && taskIds.Contains(n.TaskId.Value) && n.NotificationType == TaskNotifier.Offer, ct))
            .GroupBy(n => n.TaskId!.Value).ToDictionary(g => g.Key, g => g.OrderBy(n => n.CreatedAt).First().Severity);
        return new(zones, events, cameras, types, firstOffers);
    }
    private static string SourceCode(WorkTask task)
    {
        var (prefix, id) = task.OriginType switch
        {
            TaskOrigin.OperationalEvent => ("EVT", task.EventId),
            TaskOrigin.Incident => ("INC", task.IncidentId),
            TaskOrigin.StaffReport => ("RPT", task.StaffReportId),
            _ => ("TSK", (Guid?)task.TaskId)
        };
        return $"{prefix}-{(id ?? task.TaskId).ToString("N")[..8].ToUpperInvariant()}";
    }
    private static (string? Mode, string? Unit) Measurement(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return (null, null);
        try
        {
            using var json = JsonDocument.Parse(metadata);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);
            return (Text(root, "measurementMode"), Text(root, "thresholdUnit"));
        }
        catch (JsonException) { return (null, null); }
    }
    private static string? Text(JsonElement root, string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
