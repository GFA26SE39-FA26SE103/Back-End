using Supermarket.Domain;

namespace Supermarket.Application;

public sealed record SlaEscalationView(Guid PolicyId, string ProblemDomain, int Version, IReadOnlyList<string> WarningRoles,
    IReadOnlyList<string> BreachRoles, bool UsesDefault);
public sealed record SlaEscalationSaveRequest(int ExpectedVersion, IReadOnlyList<string>? WarningRoles, IReadOnlyList<string>? BreachRoles);

/// <summary>
/// L7: who is notified when a Task/Incident bound to this SLA policy reaches the warning milestone or breaches (BR-38–BR-41).
/// Stored as SLAPolicyAction rows (one per milestone and role) beside the WARNING/CRITICAL alert-level actions, which they
/// never match. Without escalation rows <see cref="SlaMonitor"/> keeps the default: warning → Operator, breach → Operator + Manager.
/// </summary>
public sealed class SlaEscalationPolicies(ISetupStore store, ICurrentUser current)
{
    public async Task<SlaEscalationView> Get(Guid policyId, CancellationToken ct)
    {
        UseCase.Admin(current);
        var policy = UseCase.Found(await store.Find<SlaPolicy>(policyId, ct));
        var actions = (await store.List<SlaPolicyAction>(a => a.PolicyId == policyId, ct)).Where(SlaPolicyRules.IsEscalation).ToList();
        return await View(policy, actions, ct);
    }

    public Task<SlaEscalationView> Save(Guid policyId, SlaEscalationSaveRequest request, CancellationToken ct)
    {
        UseCase.Admin(current);
        var warning = SlaPolicyRules.Roles(request.WarningRoles, "Warning escalation");
        var breach = SlaPolicyRules.Roles(request.BreachRoles, "Breach escalation");
        return store.Transaction(async () =>
        {
            var policy = UseCase.Found(await store.Find<SlaPolicy>(policyId, ct));
            if (request.ExpectedVersion != policy.Version)
                throw new ApplicationException("SLA_POLICY_VERSION_CONFLICT", "The SLA policy changed; reload and retry.");
            var roles = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (var name in warning.Concat(breach).Distinct())
            {
                var matches = await store.List<Role>(r => r.Name == name, ct);
                if (matches.Count != 1) throw new ApplicationException("ROLE_NOT_CONFIGURED", $"Role {name} is not configured.", 503);
                roles[name] = matches[0].RoleId;
            }
            foreach (var existing in (await store.List<SlaPolicyAction>(a => a.PolicyId == policyId, ct)).Where(SlaPolicyRules.IsEscalation).ToList())
                await store.Remove(existing, ct);
            var added = new List<SlaPolicyAction>();
            foreach (var (milestone, names) in new (string Milestone, string[] Roles)[] { (SlaPolicyRules.Warning, warning), (SlaPolicyRules.Breach, breach) })
                foreach (var name in names)
                {
                    var action = new SlaPolicyAction
                    {
                        ActionId = Guid.NewGuid(), PolicyId = policyId, ActionType = SlaPolicyRules.EscalationActionType(milestone),
                        TargetRoleId = roles[name], ExecutionMode = ResponsePolicyRules.Automatic,
                        ConditionJson = SlaPolicyRules.MilestoneCondition(milestone), ActionParametersJson = null, IsEnabled = true
                    };
                    await store.Add(action, ct);
                    added.Add(action);
                }
            // Escalation routing is part of the versioned policy; existing snapshots keep their timing, recipients are read live.
            policy.Version++;
            await store.Update(policy, ct);
            return await View(policy, added, ct);
        }, ct);
    }

    private async Task<SlaEscalationView> View(SlaPolicy policy, IReadOnlyList<SlaPolicyAction> actions, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var roleId in actions.Where(a => a.IsEnabled && a.TargetRoleId is not null).Select(a => a.TargetRoleId!.Value).Distinct())
            if (await store.Find<Role>(roleId, ct) is { } role) names[roleId] = role.Name;
        string[] Roles(string milestone) => actions
            .Where(a => a.IsEnabled && a.TargetRoleId is Guid id && names.ContainsKey(id) && SlaPolicyRules.Milestone(a.ConditionJson) == milestone)
            .Select(a => names[a.TargetRoleId!.Value]).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var warning = Roles(SlaPolicyRules.Warning);
        var breach = Roles(SlaPolicyRules.Breach);
        var usesDefault = warning.Length == 0 && breach.Length == 0;
        return new(policy.PolicyId, policy.ProblemDomain, policy.Version,
            warning.Length == 0 ? SlaPolicyRules.DefaultWarningRoles : warning,
            breach.Length == 0 ? SlaPolicyRules.DefaultBreachRoles : breach, usesDefault);
    }
}
