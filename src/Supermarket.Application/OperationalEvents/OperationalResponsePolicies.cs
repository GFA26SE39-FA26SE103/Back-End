using System.Text.Json;
using Supermarket.Domain;

namespace Supermarket.Application;

/// <summary>
/// Compatibility route: each alert-level entry manages one SLA action, not a separate policy.
/// SLA timing and policy enablement remain owned by the approved shared policy; its version
/// covers both actions. Both legacy enable flags gate only the selected action.
/// </summary>
public sealed class OperationalResponsePolicies(ISetupStore store, ICurrentUser currentUser)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] AlertLevels = ["CRITICAL", "WARNING"];
    private sealed record TaskParameters(string? Title, string? Instructions, string? TaskTypeCode);

    public async Task<List<ResponseTaskTypeView>> ListTaskTypes(CancellationToken ct)
    {
        UseCase.Admin(currentUser);
        var types = await store.List<TaskType>(ct: ct);
        return types.OrderBy(type => type.Code, StringComparer.Ordinal)
            .Select(type => new ResponseTaskTypeView(type.TaskTypeId, type.Code, type.Name, type.Description, type.RequiredSpecialtyId)).ToList();
    }

    public async Task<List<ResponsePolicyView>> List(Guid ruleId, CancellationToken ct)
    {
        UseCase.Admin(currentUser);
        var rule = UseCase.Found(await store.Find<MonitoringRule>(ruleId, ct));
        ResponsePolicyRules.OperationalEventRule(rule);
        var configuration = UseCase.Found(await store.Find<MonitoringConfiguration>(rule.ConfigId, ct));
        var policy = await Policy(rule, ct);
        if (policy is null) return [];
        var actions = await store.List<SlaPolicyAction>(a => a.PolicyId == policy.PolicyId, ct);
        return AlertLevels.Select(level => BuildView(rule, configuration, policy, level, Actions(actions, level))).ToList();
    }

    public Task<ResponsePolicyView> Save(Guid ruleId, string priority, ResponsePolicySaveRequest request, CancellationToken ct)
    {
        UseCase.Admin(currentUser);
        var level = ResponsePolicyRules.Priority(priority);
        if (request.Action is null)
            throw new DomainException("INVALID_POLICY_ACTION", "A response action is required.");
        var parameters = ResponsePolicyRules.TaskParameters(request.Action.Title, request.Action.Instructions, request.Action.TaskTypeCode);
        return store.Transaction(async () =>
        {
            var rule = UseCase.Found(await store.Find<MonitoringRule>(ruleId, ct));
            ResponsePolicyRules.OperationalEventRule(rule);
            var configuration = UseCase.Found(await store.Find<MonitoringConfiguration>(rule.ConfigId, ct));
            var policy = await Policy(rule, ct)
                ?? throw new ApplicationException("POLICY_MISSING", "An approved operational SLA policy must be configured first.");
            if (request.ExpectedVersion != policy.Version) throw VersionConflict();
            if (level == "CRITICAL" && request.IsEnabled && request.Action.IsEnabled && parameters.TaskTypeCode is null)
                throw new DomainException("INVALID_TASK_TYPE", "An enabled critical action requires an explicit task type code.");
            if (parameters.TaskTypeCode is not null)
            {
                var taskTypes = await store.List<TaskType>(t => t.Code == parameters.TaskTypeCode, ct);
                if (taskTypes.Count != 1)
                    throw new DomainException("INVALID_TASK_TYPE", "The response policy must reference one task type.");
            }
            var actions = Actions(await store.List<SlaPolicyAction>(a => a.PolicyId == policy.PolicyId, ct), level);
            if (actions.Count > 1) throw ActionAmbiguous();
            var action = actions.SingleOrDefault();
            var isNew = action is null;
            action ??= new SlaPolicyAction { ActionId = Guid.NewGuid(), PolicyId = policy.PolicyId };
            action.ActionType = ActionType(level);
            action.ExecutionMode = ResponsePolicyRules.Automatic;
            action.TargetRoleId = null;
            action.ConditionJson = JsonSerializer.Serialize(new { alert_level = level });
            action.ActionParametersJson = JsonSerializer.Serialize(new TaskParameters(parameters.Title, parameters.Instructions, parameters.TaskTypeCode), Json);
            action.IsEnabled = request.IsEnabled && request.Action.IsEnabled;
            if (isNew) await store.Add(action, ct);
            else await store.Update(action, ct);
            policy.Version++;
            await store.Update(policy, ct);
            // Newly added actions may not be query-visible until the transaction commits.
            return BuildView(rule, configuration, policy, level, [action]);
        }, ct);
    }

    public async Task Delete(Guid ruleId, string priority, ResponsePolicyDeleteRequest request, CancellationToken ct)
    {
        UseCase.Admin(currentUser);
        var level = ResponsePolicyRules.Priority(priority);
        await store.Transaction(async () =>
        {
            var rule = UseCase.Found(await store.Find<MonitoringRule>(ruleId, ct));
            ResponsePolicyRules.OperationalEventRule(rule);
            var policy = UseCase.Found(await Policy(rule, ct));
            if (request.ExpectedVersion != policy.Version) throw VersionConflict();
            var actions = Actions(await store.List<SlaPolicyAction>(a => a.PolicyId == policy.PolicyId, ct), level);
            if (actions.Count > 1) throw ActionAmbiguous();
            var action = UseCase.Found(actions.SingleOrDefault());
            await store.Remove(action, ct);
            // Even deleting the last action must preserve shared SLA timing and historical FKs.
            policy.Version++;
            await store.Update(policy, ct);
            return true;
        }, ct);
    }

    private async Task<SlaPolicy?> Policy(MonitoringRule rule, CancellationToken ct)
    {
        var policies = await store.List<SlaPolicy>(p => p.ProblemDomain == "OPERATIONAL_EVENT" && p.EventTypeId == rule.EventTypeId, ct);
        var active = policies.Where(p => p.IsEnabled).ToList();
        if (active.Count > 1 || active.Count == 0 && policies.Count > 1)
            throw new ApplicationException("POLICY_AMBIGUOUS", "More than one operational SLA policy matches this event type.");
        // A unique disabled policy may be inspected/edited, never implicitly enabled.
        return active.SingleOrDefault() ?? policies.SingleOrDefault();
    }

    private static List<SlaPolicyAction> Actions(IEnumerable<SlaPolicyAction> actions, string level)
        => actions.Where(a => OperationalSlaPolicyRules.AlertLevel(a.ConditionJson) == level).ToList();

    private static string ActionType(string level)
        => level == "WARNING" ? OperationalSlaPolicyRules.NotifyOperator : OperationalSlaPolicyRules.CreateTask;

    private static ResponsePolicyView BuildView(MonitoringRule rule, MonitoringConfiguration configuration, SlaPolicy policy,
        string level, IReadOnlyList<SlaPolicyAction> actions)
    {
        var issues = new List<string>();
        if (!ResponsePolicyRules.IsOperationalEventRule(rule)) issues.Add("INVALID_POLICY_RULE");
        if (!rule.Enabled) issues.Add("RULE_DISABLED");
        if (configuration.Status != "ACTIVE") issues.Add("CONFIGURATION_NOT_ACTIVE");
        if (!policy.IsEnabled) issues.Add("POLICY_DISABLED");
        if (!OperationalSlaPolicyRules.Valid(policy)) issues.Add("SLA_POLICY_INVALID");
        if (actions.Count == 0) issues.Add("ACTION_MISSING");
        if (actions.Count > 1) issues.Add("ACTION_AMBIGUOUS");
        var action = actions.Count == 1 ? actions[0] : null;
        if (action is not null)
        {
            if (!action.IsEnabled) issues.Add("ACTION_DISABLED");
            if (action.ExecutionMode != ResponsePolicyRules.Automatic) issues.Add("ACTION_NOT_AUTOMATIC");
            if (action.ActionType != ActionType(level)) issues.Add("ACTION_TYPE_INVALID");
        }
        var parameters = Parameters(action?.ActionParametersJson);
        if (level == "CRITICAL" && action is not null && string.IsNullOrWhiteSpace(parameters?.TaskTypeCode))
            issues.Add("TASK_TYPE_MISSING");
        return new(policy.PolicyId, rule.RuleId, level, policy.IsEnabled && action?.IsEnabled == true, policy.Version, policy.UpdatedAt,
            action is null ? null : new(action.ActionId, action.IsEnabled, action.ActionType, action.ExecutionMode, parameters?.Title, parameters?.Instructions, parameters?.TaskTypeCode),
            level == "CRITICAL" && issues.Count == 0, issues);
    }

    private static TaskParameters? Parameters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<TaskParameters>(json, Json); }
        catch (JsonException) { return null; }
    }

    private static ApplicationException VersionConflict()
        => new("POLICY_VERSION_CONFLICT", "The response policy changed; reload and retry.");

    private static ApplicationException ActionAmbiguous()
        => new("POLICY_ACTION_AMBIGUOUS", "More than one action exists for this policy and alert level.");
}
