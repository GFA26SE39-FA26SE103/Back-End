using System.Text.Json;
using Supermarket.Domain;

namespace Supermarket.Application;

/// <summary>Admin management for the currently executable OperationalEvent → automatic Task policy.</summary>
public sealed class OperationalResponsePolicies(ISetupStore store, ICurrentUser currentUser)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record TaskParameters(string? Title, string? Instructions);

    public async Task<List<ResponsePolicyView>> List(Guid ruleId, CancellationToken ct)
    {
        UseCase.Admin(currentUser);
        var rule = UseCase.Found(await store.Find<MonitoringRule>(ruleId, ct));
        ResponsePolicyRules.OperationalEventRule(rule);
        var configuration = UseCase.Found(await store.Find<MonitoringConfiguration>(rule.ConfigId, ct));
        var result = new List<ResponsePolicyView>();
        foreach (var policy in (await store.List<ResponsePolicy>(p => p.RuleId == ruleId, ct)).OrderBy(p => p.EventPriority))
            result.Add(await View(rule, configuration, policy, ct));
        return result;
    }

    public Task<ResponsePolicyView> Save(Guid ruleId, string priority, ResponsePolicySaveRequest request, CancellationToken ct)
    {
        UseCase.Admin(currentUser);
        var normalizedPriority = ResponsePolicyRules.Priority(priority);
        if (request.Action is null)
            throw new DomainException("INVALID_POLICY_ACTION", "An automatic-task action is required.");
        var parameters = ResponsePolicyRules.TaskParameters(request.Action.Title, request.Action.Instructions);
        return store.Transaction(async () =>
        {
            var rule = UseCase.Found(await store.Find<MonitoringRule>(ruleId, ct));
            ResponsePolicyRules.OperationalEventRule(rule);
            var configuration = UseCase.Found(await store.Find<MonitoringConfiguration>(rule.ConfigId, ct));
            var existing = await store.List<ResponsePolicy>(p => p.RuleId == ruleId && p.EventPriority == normalizedPriority, ct);
            if (existing.Count > 1)
                throw new ApplicationException("POLICY_AMBIGUOUS", "More than one policy exists for this rule and priority.");

            var policy = existing.SingleOrDefault();
            if (policy is null)
            {
                if (request.ExpectedVersion is not null)
                    throw VersionConflict();
                policy = new ResponsePolicy
                {
                    PolicyId = Guid.NewGuid(), RuleId = ruleId, EventPriority = normalizedPriority,
                    IsEnabled = request.IsEnabled, Version = 1
                };
                await store.Add(policy, ct);
            }
            else
            {
                if (request.ExpectedVersion != policy.Version)
                    throw VersionConflict();
                policy.IsEnabled = request.IsEnabled;
                policy.Version++;
                await store.Update(policy, ct);
            }

            var managedActions = await store.List<ResponsePolicyAction>(a => a.PolicyId == policy.PolicyId && a.ActionType == ResponsePolicyRules.CreateOperationalTask, ct);
            if (managedActions.Count > 1)
                throw new ApplicationException("POLICY_ACTION_AMBIGUOUS", "More than one automatic-task action exists for this policy.");
            var action = managedActions.SingleOrDefault();
            var actionParameters = JsonSerializer.Serialize(new TaskParameters(parameters.Title, parameters.Instructions), Json);
            if (action is null)
            {
                action = new ResponsePolicyAction
                {
                    ActionId = Guid.NewGuid(), PolicyId = policy.PolicyId,
                    ActionType = ResponsePolicyRules.CreateOperationalTask, ExecutionMode = ResponsePolicyRules.Automatic,
                    ActionParametersJson = actionParameters, IsEnabled = request.Action.IsEnabled
                };
                await store.Add(action, ct);
            }
            else
            {
                action.ExecutionMode = ResponsePolicyRules.Automatic;
                action.TargetRoleId = null;
                action.ConditionJson = null;
                action.ActionParametersJson = actionParameters;
                action.IsEnabled = request.Action.IsEnabled;
                await store.Update(action, ct);
            }
            return BuildView(rule, configuration, policy, [action]);
        }, ct);
    }

    public async Task Delete(Guid ruleId, string priority, ResponsePolicyDeleteRequest request, CancellationToken ct)
    {
        UseCase.Admin(currentUser);
        var normalizedPriority = ResponsePolicyRules.Priority(priority);
        await store.Transaction(async () =>
        {
            var rule = UseCase.Found(await store.Find<MonitoringRule>(ruleId, ct));
            ResponsePolicyRules.OperationalEventRule(rule);
            var existing = await store.List<ResponsePolicy>(p => p.RuleId == ruleId && p.EventPriority == normalizedPriority, ct);
            if (existing.Count > 1)
                throw new ApplicationException("POLICY_AMBIGUOUS", "More than one policy exists for this rule and priority.");
            var policy = UseCase.Found(existing.SingleOrDefault());
            if (request.ExpectedVersion != policy.Version)
                throw VersionConflict();
            foreach (var action in await store.List<ResponsePolicyAction>(a => a.PolicyId == policy.PolicyId, ct))
                await store.Remove(action, ct);
            await store.Remove(policy, ct);
            return true;
        }, ct);
    }

    private async Task<ResponsePolicyView> View(MonitoringRule rule, MonitoringConfiguration configuration, ResponsePolicy policy, CancellationToken ct)
    {
        var actions = await store.List<ResponsePolicyAction>(a => a.PolicyId == policy.PolicyId && a.ActionType == ResponsePolicyRules.CreateOperationalTask, ct);
        return BuildView(rule, configuration, policy, actions);
    }

    private static ResponsePolicyView BuildView(MonitoringRule rule, MonitoringConfiguration configuration, ResponsePolicy policy, IReadOnlyList<ResponsePolicyAction> actions)
    {
        var issues = new List<string>();
        if (!rule.Enabled) issues.Add("RULE_DISABLED");
        if (configuration.Status != "ACTIVE") issues.Add("CONFIGURATION_NOT_ACTIVE");
        if (!policy.IsEnabled) issues.Add("POLICY_DISABLED");
        if (actions.Count == 0) issues.Add("ACTION_MISSING");
        if (actions.Count > 1) issues.Add("ACTION_AMBIGUOUS");
        var action = actions.Count == 1 ? actions[0] : null;
        if (action is not null)
        {
            if (!action.IsEnabled) issues.Add("ACTION_DISABLED");
            if (action.ExecutionMode != ResponsePolicyRules.Automatic) issues.Add("ACTION_NOT_AUTOMATIC");
        }
        var parameters = Parameters(action?.ActionParametersJson);
        return new(policy.PolicyId, policy.RuleId, policy.EventPriority, policy.IsEnabled, policy.Version, policy.UpdatedAt,
            action is null ? null : new(action.ActionId, action.IsEnabled, action.ActionType, action.ExecutionMode, parameters?.Title, parameters?.Instructions),
            issues.Count == 0, issues);
    }

    private static TaskParameters? Parameters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<TaskParameters>(json, Json); }
        catch (JsonException) { return null; }
    }

    private static ApplicationException VersionConflict()
        => new("POLICY_VERSION_CONFLICT", "The response policy changed; reload and retry.");
}
