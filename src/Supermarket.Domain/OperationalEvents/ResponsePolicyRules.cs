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

    public static (string? Title, string? Instructions, string? TaskTypeCode) TaskParameters(string? title, string? instructions, string? taskTypeCode)
    {
        title = Normalize(title);
        instructions = Normalize(instructions);
        taskTypeCode = Normalize(taskTypeCode)?.ToUpperInvariant();
        Rules.Optional(title, 200, "Task title");
        Rules.Optional(instructions, 4000, "Task instructions");
        Rules.Optional(taskTypeCode, 50, "Task type code");
        return (title, instructions, taskTypeCode);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
