using Supermarket.Domain;

namespace Supermarket.Application;

public sealed record SlaPolicySummary(Guid PolicyId, bool IsEnabled, int Version, string PriorityCode, int ResponseTargetSec,
    int ResolutionTargetSec, int OfferTimeoutSec, int WarningPercent, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record SlaTimingSaveRequest(int? ExpectedVersion, bool IsEnabled, string PriorityCode, int ResponseTargetSec,
    int ResolutionTargetSec, int OfferTimeoutSec, int WarningPercent);
public sealed record OperationalEventSlaPolicyView(Guid EventTypeId, string Code, string Name, string MeasurementType, string Status,
    SlaPolicySummary? Policy, IReadOnlyList<string> Issues);
public sealed record TaskTypeSlaPolicyView(Guid TaskTypeId, string Code, string Name, string SourceCategory,
    SlaPolicySummary? Policy, IReadOnlyList<string> Issues);

/// <summary>L7: Admin creates or edits the one SLA timing policy of each OperationalEventType (problem_domain OPERATIONAL_EVENT).
/// WARNING/CRITICAL actions stay on <see cref="OperationalResponsePolicies"/>; escalation on <see cref="SlaEscalationPolicies"/>.</summary>
public sealed class OperationalEventSlaPolicies(ISetupStore store, ICurrentUser current)
{
    public async Task<OperationalEventSlaPolicyView[]> List(CancellationToken ct)
    {
        UseCase.Admin(current);
        var policies = await store.List<SlaPolicy>(p => p.ProblemDomain == SlaPolicyRules.OperationalEventDomain && p.EventTypeId != null, ct);
        return (await store.List<OperationalEventType>(ct: ct)).OrderBy(t => t.Code, StringComparer.Ordinal)
            .Select(t => View(t, policies.Where(p => p.EventTypeId == t.EventTypeId).ToList())).ToArray();
    }

    public Task<OperationalEventSlaPolicyView> Save(Guid eventTypeId, SlaTimingSaveRequest request, CancellationToken ct)
    {
        UseCase.Admin(current);
        var priority = ScopedSlaPolicyWriter.Validate(request);
        return store.Transaction(async () =>
        {
            var type = UseCase.Found(await store.Find<OperationalEventType>(eventTypeId, ct));
            var matches = await store.List<SlaPolicy>(p => p.ProblemDomain == SlaPolicyRules.OperationalEventDomain && p.EventTypeId == eventTypeId, ct);
            var policy = await ScopedSlaPolicyWriter.Upsert(store, matches, request, priority, () => new SlaPolicy
            {
                PolicyId = Guid.NewGuid(), ProblemDomain = SlaPolicyRules.OperationalEventDomain, EventTypeId = eventTypeId, Version = 1
            }, p => { p.IncidentTypeId = null; p.TaskTypeId = null; }, ct);
            return View(type, [policy]);
        }, ct);
    }

    private static OperationalEventSlaPolicyView View(OperationalEventType type, IReadOnlyList<SlaPolicy> policies)
    {
        var (summary, issues) = ScopedSlaPolicyWriter.Summary(policies);
        return new(type.EventTypeId, type.Code, type.Name, type.MeasurementType, type.Status, summary, issues);
    }
}

/// <summary>L1/L8: Admin creates or edits the SLA policy used by StaffReport routine and Operator manual Tasks of one TaskType
/// (problem_domain STANDALONE_TASK, read by <see cref="StandaloneTaskSlaPolicyService"/>).</summary>
public sealed class TaskTypeSlaPolicies(ISetupStore store, ICurrentUser current)
{
    public async Task<TaskTypeSlaPolicyView[]> List(CancellationToken ct)
    {
        UseCase.Admin(current);
        var policies = await store.List<SlaPolicy>(p => p.ProblemDomain == SlaPolicyRules.StandaloneTaskDomain && p.TaskTypeId != null, ct);
        return (await store.List<TaskType>(ct: ct)).OrderBy(t => t.Code, StringComparer.Ordinal)
            .Select(t => View(t, policies.Where(p => p.TaskTypeId == t.TaskTypeId).ToList())).ToArray();
    }

    public Task<TaskTypeSlaPolicyView> Save(Guid taskTypeId, SlaTimingSaveRequest request, CancellationToken ct)
    {
        UseCase.Admin(current);
        var priority = ScopedSlaPolicyWriter.Validate(request);
        return store.Transaction(async () =>
        {
            var type = UseCase.Found(await store.Find<TaskType>(taskTypeId, ct));
            var matches = await store.List<SlaPolicy>(p => p.ProblemDomain == SlaPolicyRules.StandaloneTaskDomain && p.TaskTypeId == taskTypeId, ct);
            var policy = await ScopedSlaPolicyWriter.Upsert(store, matches, request, priority, () => new SlaPolicy
            {
                PolicyId = Guid.NewGuid(), ProblemDomain = SlaPolicyRules.StandaloneTaskDomain, TaskTypeId = taskTypeId, Version = 1
            }, p => { p.EventTypeId = null; p.IncidentTypeId = null; }, ct);
            return View(type, [policy]);
        }, ct);
    }

    private static TaskTypeSlaPolicyView View(TaskType type, IReadOnlyList<SlaPolicy> policies)
    {
        var (summary, issues) = ScopedSlaPolicyWriter.Summary(policies);
        return new(type.TaskTypeId, type.Code, type.Name, type.SourceCategory, summary, issues);
    }
}

internal static class ScopedSlaPolicyWriter
{
    public static string Validate(SlaTimingSaveRequest request)
    {
        var priority = SlaPolicyRules.Priority(request.PriorityCode);
        SlaPolicyRules.Timing(request.ResponseTargetSec, request.ResolutionTargetSec, request.OfferTimeoutSec, request.WarningPercent);
        return priority;
    }

    /// <summary>Creates version 1 or bumps the version of the single scoped policy. Existing Task/Incident snapshots are never rewritten.</summary>
    public static async Task<SlaPolicy> Upsert(ISetupStore store, List<SlaPolicy> matches, SlaTimingSaveRequest request, string priority,
        Func<SlaPolicy> create, Action<SlaPolicy> clearOtherScopes, CancellationToken ct)
    {
        if (matches.Count > 1)
            throw new ApplicationException("SLA_POLICY_AMBIGUOUS", "More than one SLA policy exists for this scope.", 503);
        var policy = matches.SingleOrDefault();
        if (policy is null)
        {
            if (request.ExpectedVersion is not null) throw VersionConflict();
            policy = create();
            Apply(policy, request, priority);
            clearOtherScopes(policy);
            await store.Add(policy, ct);
            return policy;
        }
        if (request.ExpectedVersion != policy.Version) throw VersionConflict();
        policy.Version++;
        Apply(policy, request, priority);
        clearOtherScopes(policy);
        await store.Update(policy, ct);
        return policy;
    }

    public static (SlaPolicySummary? Summary, IReadOnlyList<string> Issues) Summary(IReadOnlyList<SlaPolicy> policies)
    {
        if (policies.Count == 0) return (null, new[] { "POLICY_MISSING" });
        if (policies.Count > 1) return (null, new[] { "POLICY_AMBIGUOUS" });
        var p = policies[0];
        var issues = new List<string>();
        if (!p.IsEnabled) issues.Add("POLICY_DISABLED");
        if (!SlaPolicyRules.IsPriority(p.PriorityCode)) issues.Add("PRIORITY_INVALID");
        if (p.ResponseTargetSec <= 0 || p.ResolutionTargetSec < p.ResponseTargetSec || p.OfferTimeoutSec <= 0 || p.WarningPercent is <= 0 or >= 100)
            issues.Add("TIMING_INVALID");
        return (new SlaPolicySummary(p.PolicyId, p.IsEnabled, p.Version, p.PriorityCode, p.ResponseTargetSec, p.ResolutionTargetSec, p.OfferTimeoutSec,
            p.WarningPercent, p.CreatedAt, p.UpdatedAt), issues);
    }

    private static void Apply(SlaPolicy policy, SlaTimingSaveRequest request, string priority)
    {
        policy.IsEnabled = request.IsEnabled; policy.PriorityCode = priority; policy.ResponseTargetSec = request.ResponseTargetSec;
        policy.ResolutionTargetSec = request.ResolutionTargetSec; policy.OfferTimeoutSec = request.OfferTimeoutSec; policy.WarningPercent = request.WarningPercent;
    }

    public static ApplicationException VersionConflict() => new("SLA_POLICY_VERSION_CONFLICT", "The SLA policy changed; reload and retry.");
}
