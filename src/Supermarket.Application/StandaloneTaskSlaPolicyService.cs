using System.Text.Json;
using Supermarket.Domain;

namespace Supermarket.Application;

public sealed record StandaloneTaskSlaSnapshot(Guid PolicyId, int PolicyVersion, Guid TaskTypeId,
    string PriorityCode, int ResponseTargetSec, int ResolutionTargetSec, int OfferTimeoutSec,
    int WarningPercent, DateTime AppliedAt, DateTime ResponseDeadlineAt, DateTime ResolutionDeadlineAt);

public sealed class StandaloneTaskSlaPolicyService(ISetupStore store, IClock clock)
{
    public async Task Apply(WorkTask task, CancellationToken ct)
    {
        if (task.OriginType is not (TaskOrigin.StaffReport or TaskOrigin.Manual) || task.TaskTypeId is not Guid typeId
            || task.SlaPolicyId is not null || task.SlaSnapshotJson is not null)
            throw new ApplicationException("STANDALONE_TASK_INVALID", "A standalone Task requires one TaskType and no existing SLA.", 422);
        var matches = await store.List<SlaPolicy>(p => p.IsEnabled && p.ProblemDomain == "STANDALONE_TASK" && p.TaskTypeId == typeId, ct);
        if (matches.Count == 0)
            throw new ApplicationException("TASK_SLA_POLICY_MISSING", "No enabled TaskType SLA policy is configured.", 422);
        if (matches.Count > 1)
            throw new ApplicationException("TASK_SLA_POLICY_AMBIGUOUS", "More than one TaskType SLA policy is enabled.", 503);
        var policy = matches[0];
        if (policy.PolicyId == Guid.Empty || policy.Version <= 0 || policy.TaskTypeId != typeId
            || policy.EventTypeId is not null || policy.IncidentTypeId is not null
            || policy.PriorityCode is not ("LOW" or "MEDIUM" or "HIGH" or "CRITICAL")
            || policy.ResponseTargetSec <= 0 || policy.ResolutionTargetSec < policy.ResponseTargetSec
            || policy.OfferTimeoutSec <= 0 || policy.WarningPercent is <= 0 or >= 100)
            throw new ApplicationException("TASK_SLA_POLICY_INVALID", "The TaskType SLA policy is invalid.", 503);
        var now = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        var snapshot = new StandaloneTaskSlaSnapshot(policy.PolicyId, policy.Version, typeId, policy.PriorityCode,
            policy.ResponseTargetSec, policy.ResolutionTargetSec, policy.OfferTimeoutSec, policy.WarningPercent,
            now, now.AddSeconds(policy.ResponseTargetSec), now.AddSeconds(policy.ResolutionTargetSec));
        task.Priority = policy.PriorityCode;
        task.SlaPolicyId = policy.PolicyId;
        task.SlaSnapshotJson = JsonSerializer.Serialize(snapshot, IncidentSlaPolicyService.JsonOptions);
        task.ResponseDeadlineAt = snapshot.ResponseDeadlineAt;
        task.ResolutionDeadlineAt = snapshot.ResolutionDeadlineAt;
    }
}
