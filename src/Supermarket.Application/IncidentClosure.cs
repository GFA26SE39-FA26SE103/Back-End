using System.Text.Json;
using Supermarket.Domain;

namespace Supermarket.Application;

/// <summary>
/// BR-22: an Incident closes only when every mandatory corrective Task is Verified. Mitigation (BR-21) is a separate
/// milestone and is not set here. Independent closure conditions beyond the mandatory Tasks are not modelled yet, so
/// the mandatory Tasks are the only blocker.
/// </summary>
public sealed class IncidentClosure(ISetupStore store, IClock clock, TaskNotifier notifier)
{
    public const string Closed = "INCIDENT_CLOSED";
    public const string ClosedStatus = "CLOSED";
    public const string AllMandatoryVerified = "ALL_MANDATORY_TASKS_VERIFIED";
    private static readonly string[] EndedStatuses = [ClosedStatus, "DISMISSED", "CANCELLED"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Called inside the transaction that verified <paramref name="verified"/>. Closes its Incident when that was the last
    /// mandatory Task still open; returns whether the Incident was closed.
    /// </summary>
    public async Task<bool> AfterTaskVerified(WorkTask verified, Guid actorUserId, CancellationToken ct)
    {
        if (verified.IncidentId is not { } incidentId) return false;
        var incident = await store.Find<Incident>(incidentId, ct);
        if (incident is null || incident.ClosedAt is not null || EndedStatuses.Contains(incident.Status)) return false;

        var mandatory = (await store.List<WorkTask>(t => t.IncidentId == incidentId && t.IsMandatory, ct))
            .Where(t => t.TaskId != verified.TaskId).Append(verified).ToList();
        // Only mandatory Tasks block closure; a cancelled mandatory Task is not Verified, so the Operator decides.
        if (!verified.IsMandatory || mandatory.Any(t => t.Status != WorkTaskStatus.Verified)) return false;

        var before = State(incident);
        var now = clock.UtcNow;
        incident.Status = ClosedStatus;
        incident.StatusReason = AllMandatoryVerified;
        incident.ClosedAt = now;
        incident.UpdatedAt = now;
        await store.Update(incident, ct);
        await store.Add(new AuditLog
        {
            AuditLogId = Guid.NewGuid(), UserId = actorUserId, Action = "CLOSE_INCIDENT", EntityType = "Incident", EntityId = incident.IncidentId,
            OldValuesJson = JsonSerializer.Serialize(before, Json), NewValuesJson = JsonSerializer.Serialize(State(incident), Json),
            Reason = AllMandatoryVerified, CreatedAt = now, ResultStatus = "SUCCESS"
        }, ct);
        await notifier.Send(Closed, "Incident closed", $"{incident.Title}: every mandatory Task is verified, so the Incident is closed.",
            verified, await notifier.Operators(ct), "OPERATOR", ct);
        return true;
    }

    private static object State(Incident incident) => new { incident.Status, incident.StatusReason, incident.ClosedAt };
}
