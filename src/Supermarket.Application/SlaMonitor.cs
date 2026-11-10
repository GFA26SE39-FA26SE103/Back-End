using System.Text.Json;
using Supermarket.Domain;

namespace Supermarket.Application;

/// <summary>
/// BR-38–BR-41: raises SLA warning (default 80 %, the snapshot's warning percentage) and breach (100 %) milestones for
/// open Tasks and Incidents. Each milestone is one Notification row, written at most once, so it stays auditable after
/// late completion. Warnings go to Operators; breaches escalate to Operators and Managers. Offer expiry is a dispatch
/// event and is not handled here.
/// </summary>
public sealed class SlaMonitor(ISetupStore store, IClock clock, TaskNotifier notifier, ITaskSchema schema)
{
    public const string ResponseWarning = "SLA_RESPONSE_WARNING", ResponseBreach = "SLA_RESPONSE_BREACH",
        ResolutionWarning = "SLA_RESOLUTION_WARNING", ResolutionBreach = "SLA_RESOLUTION_BREACH",
        IncidentWarning = "INCIDENT_SLA_WARNING", IncidentBreach = "INCIDENT_SLA_BREACH";
    public const int DefaultWarningPercent = 80;
    private static readonly string[] TaskTypes = [ResponseWarning, ResponseBreach, ResolutionWarning, ResolutionBreach];
    private static readonly string[] IncidentTypes = [IncidentWarning, IncidentBreach];
    private static readonly string[] EndedIncident = ["CLOSED", "DISMISSED", "CANCELLED"];

    /// <summary>Writes the milestones that are due now; returns how many were written.</summary>
    public async Task<int> Tick(CancellationToken ct)
    {
        if (!await schema.Ready(ct)) return 0;
        var now = clock.UtcNow;
        var written = 0;
        List<Guid>? operators = null, escalation = null;
        async Task<List<Guid>> Operators() => operators ??= await notifier.Operators(ct);
        async Task<List<Guid>> Escalation() => escalation ??= [.. await Operators(), .. await notifier.RoleUsers("MANAGER", ct)];

        var tasks = await store.List<WorkTask>(t => t.Status != WorkTaskStatus.Verified && t.Status != WorkTaskStatus.Cancelled
            && (t.ResponseDeadlineAt != null || t.ResolutionDeadlineAt != null), ct);
        var taskIds = tasks.Select(t => t.TaskId).ToList();
        var taskDone = (await store.List<Notification>(n => n.TaskId != null && taskIds.Contains(n.TaskId.Value) && TaskTypes.Contains(n.NotificationType), ct))
            .Select(n => (n.TaskId!.Value, n.NotificationType)).ToHashSet();
        foreach (var task in tasks)
        {
            var (start, percent) = Clock(task.SlaSnapshotJson, task.CreatedAt);
            // Response runs until the first valid acceptance (BR-39).
            if (task.AcceptedAt is null && task.ResponseDeadlineAt is { } response)
                foreach (var (type, label, due) in Milestones(ResponseWarning, ResponseBreach, start, response, percent))
                    if (now >= due && taskDone.Add((task.TaskId, type)))
                    {
                        var breach = type == ResponseBreach;
                        await Write(type, breach ? "Task response SLA breached" : "Task response SLA warning",
                            $"{task.Title}: {label} — nobody has accepted it yet (deadline {response:O}).", task.TaskId, task.IncidentId, task.Priority,
                            breach ? await Escalation() : await Operators(), breach ? "SLA_ESCALATION" : "SLA_WARNING", now, ct);
                        written++;
                    }
            // Resolution runs until Verified (BR-39); retry, reassignment and pending verification do not pause it.
            if (task.ResolutionDeadlineAt is { } resolution)
                foreach (var (type, label, due) in Milestones(ResolutionWarning, ResolutionBreach, start, resolution, percent))
                    if (now >= due && taskDone.Add((task.TaskId, type)))
                    {
                        var breach = type == ResolutionBreach;
                        await Write(type, breach ? "Task resolution SLA breached" : "Task resolution SLA warning",
                            $"{task.Title}: {label} — the Task is still {task.Status} (deadline {resolution:O}).", task.TaskId, task.IncidentId, task.Priority,
                            breach ? await Escalation() : await Operators(), breach ? "SLA_ESCALATION" : "SLA_WARNING", now, ct);
                        written++;
                    }
        }

        var incidents = (await store.List<Incident>(i => i.ClosedAt == null && i.ResolutionDeadlineAt != null, ct))
            .Where(i => !EndedIncident.Contains(i.Status)).ToList();
        var incidentIds = incidents.Select(i => i.IncidentId).ToList();
        var incidentDone = (await store.List<Notification>(n => n.TaskId == null && n.IncidentId != null && incidentIds.Contains(n.IncidentId.Value)
            && IncidentTypes.Contains(n.NotificationType), ct)).Select(n => (n.IncidentId!.Value, n.NotificationType)).ToHashSet();
        foreach (var incident in incidents)
        {
            var (start, percent) = Clock(incident.SlaSnapshotJson, incident.CreatedAt);
            var deadline = incident.ResolutionDeadlineAt!.Value;
            foreach (var (type, label, due) in Milestones(IncidentWarning, IncidentBreach, start, deadline, percent))
                if (now >= due && incidentDone.Add((incident.IncidentId, type)))
                {
                    var breach = type == IncidentBreach;
                    await Write(type, breach ? "Incident resolution SLA breached" : "Incident resolution SLA warning",
                        $"{incident.Title}: {label} — the Incident is still open (deadline {deadline:O}).", null, incident.IncidentId, Priority(incident.SlaSnapshotJson),
                        breach ? await Escalation() : await Operators(), breach ? "SLA_ESCALATION" : "SLA_WARNING", now, ct);
                    written++;
                }
        }
        return written;
    }

    /// <summary>Warning at the warning percentage of the window, breach at 100 %. A breach found first still records the warning, so both stay auditable.</summary>
    private static IEnumerable<(string Type, string Label, DateTime Due)> Milestones(string warning, string breach, DateTime start, DateTime deadline, int percent)
    {
        var window = deadline - start;
        var warnAt = window > TimeSpan.Zero ? start + TimeSpan.FromTicks(window.Ticks * percent / 100) : deadline;
        yield return (warning, $"{percent}% of the SLA time has passed", warnAt);
        yield return (breach, "the SLA deadline has passed", deadline);
    }

    /// <summary>The clock starts when the policy was applied (Task/Incident creation); the warning percentage comes from the bound snapshot.</summary>
    private static (DateTime Start, int Percent) Clock(string? snapshotJson, DateTime createdAt)
    {
        var start = createdAt; var percent = DefaultWarningPercent;
        if (snapshotJson is { Length: > 0 })
        {
            try
            {
                using var doc = JsonDocument.Parse(snapshotJson);
                var root = doc.RootElement;
                if (Property(root, "appliedAt") is { ValueKind: JsonValueKind.String } applied && applied.TryGetDateTime(out var at))
                    start = DateTime.SpecifyKind(at.Kind == DateTimeKind.Local ? at.ToUniversalTime() : at, DateTimeKind.Utc);
                if (Property(root, "warningPercent") is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var p) && p is > 0 and < 100)
                    percent = p;
            }
            catch (JsonException) { }
        }
        return (start, percent);
    }

    private static string? Priority(string? snapshotJson)
    {
        if (snapshotJson is not { Length: > 0 }) return null;
        try
        {
            using var doc = JsonDocument.Parse(snapshotJson);
            return Property(doc.RootElement, "priorityCode") is { ValueKind: JsonValueKind.String } code ? code.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static JsonElement? Property(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in root.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }

    private async Task Write(string type, string title, string message, Guid? taskId, Guid? incidentId, string? severity,
        IEnumerable<Guid> recipients, string reason, DateTime now, CancellationToken ct)
    {
        var notification = new Notification { NotificationId = Guid.NewGuid(), TaskId = taskId, IncidentId = incidentId, NotificationType = type,
            Title = title, Message = message.Length > 4000 ? message[..4000] : message, Severity = severity, CreatedAt = now };
        await store.Add(notification, ct);
        foreach (var userId in recipients.Distinct())
            await store.Add(new NotificationRecipient { NotificationRecipientId = Guid.NewGuid(), NotificationId = notification.NotificationId, UserId = userId,
                DeliveryChannel = "IN_APP", DeliveryStatus = "SENT", RecipientReason = reason, DeliveredAt = now }, ct);
    }
}
