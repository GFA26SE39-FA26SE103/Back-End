namespace Supermarket.Domain;

/// <summary>Invariants for OperationalEvent response policies that create operational tasks.</summary>
public static class ResponsePolicyRules
{
    public const string CreateOperationalTask = "CREATE_OPERATIONAL_TASK";
    public const string Automatic = "AUTOMATIC";

    public static string Priority(string? value)
    {
        var priority = value?.Trim().ToUpperInvariant() ?? "";
        Rules.Status(priority, "WARNING", "CRITICAL");
        return priority;
    }

    public static void OperationalEventRule(MonitoringRule rule)
        => Rules.Require(IsOperationalEventRule(rule),
            "INVALID_POLICY_RULE", "Response policies can target only operational-event rules.");

    public static bool IsOperationalEventRule(MonitoringRule rule)
        => rule.OutcomeType == "OPERATIONAL_EVENT" && rule.EventTypeId is not null && rule.IncidentTypeId is null;

    public static (string? Title, string? Instructions) TaskParameters(string? title, string? instructions)
    {
        title = Normalize(title);
        instructions = Normalize(instructions);
        Rules.Optional(title, 200, "Task title");
        Rules.Optional(instructions, 4000, "Task instructions");
        return (title, instructions);
    }

    public static IReadOnlyList<string> ReadinessIssues(MonitoringRule rule, MonitoringConfiguration configuration, ResponsePolicy policy,
        IReadOnlyCollection<ResponsePolicyAction> actions)
    {
        var issues = new List<string>();
        if (!IsOperationalEventRule(rule)) issues.Add("INVALID_POLICY_RULE");
        if (!rule.Enabled) issues.Add("RULE_DISABLED");
        if (configuration.Status != "ACTIVE") issues.Add("CONFIGURATION_NOT_ACTIVE");
        if (!policy.IsEnabled) issues.Add("POLICY_DISABLED");
        if (actions.Count == 0) issues.Add("ACTION_MISSING");
        if (actions.Count > 1) issues.Add("ACTION_AMBIGUOUS");
        var action = actions.Count == 1 ? actions.Single() : null;
        if (action is not null)
        {
            if (!action.IsEnabled) issues.Add("ACTION_DISABLED");
            if (action.ExecutionMode != Automatic) issues.Add("ACTION_NOT_AUTOMATIC");
        }
        return issues;
    }

    public static ResponsePolicyAction? ReadyAutomaticTaskAction(MonitoringRule rule, MonitoringConfiguration configuration, ResponsePolicy policy,
        IReadOnlyCollection<ResponsePolicyAction> actions)
        => ReadinessIssues(rule, configuration, policy, actions).Count == 0 ? actions.Single() : null;

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
