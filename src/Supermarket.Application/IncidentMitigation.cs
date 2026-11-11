using System.Text.Json;
using Supermarket.Domain;

namespace Supermarket.Application;

/// <summary>BR-21: TT-12 is the only designated mitigation Task; submission alone never mitigates an Incident.</summary>
public sealed class IncidentMitigation(ISetupStore store)
{
    public async Task AfterManualTaskVerified(WorkTask task, Guid actorUserId, CancellationToken ct)
    {
        if (task.OriginType != TaskOrigin.Incident || task.IncidentId is not { } incidentId
            || task.Status != WorkTaskStatus.Verified || task.VerificationMode != TaskVerificationMode.Manual
            || task.VerifiedByUserId != actorUserId || task.VerifiedAt is not { } verifiedAt
            || task.TaskTypeId is not { } taskTypeId) return;
        if ((await store.Find<TaskType>(taskTypeId, ct))?.Code != "TT-12") return;
        var incident = await store.Find<Incident>(incidentId, ct);
        if (incident is null || incident.ZoneId != task.ZoneId || incident.MitigatedAt is not null
            || incident.ClosedAt is not null || incident.Status is "CLOSED" or "DISMISSED" or "CANCELLED") return;

        // The caller's verification transaction owns both writes and the audit. Keep resolution/SLA independent.
        incident.MitigatedAt = verifiedAt;
        // Preserve UpdatedAt as the original concurrency token; the store advances it on update.
        await store.Update(incident, ct);
        await store.Add(new AuditLog
        {
            AuditLogId = Guid.NewGuid(), UserId = actorUserId, Action = "MITIGATE_INCIDENT",
            EntityType = "Incident", EntityId = incidentId, Reason = "TT-12_MANUAL_VERIFIED",
            OldValuesJson = "{\"mitigatedAt\":null}",
            NewValuesJson = JsonSerializer.Serialize(new { mitigatedAt = verifiedAt, taskId = task.TaskId, taskTypeCode = "TT-12" }),
            CreatedAt = verifiedAt, ResultStatus = "SUCCESS"
        }, ct);
    }
}
