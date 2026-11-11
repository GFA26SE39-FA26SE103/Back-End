using System.Text.Json;

namespace Supermarket.Domain;

/// <summary>
/// Shared SLA policy invariants (team decision 11/11/2026: priority codes are LOW/MEDIUM/HIGH/CRITICAL, not P1–P4)
/// and the SLA milestone escalation actions stored in SLAPolicyAction.
/// </summary>
public static class SlaPolicyRules
{
    public const string OperationalEventDomain = "OPERATIONAL_EVENT", IncidentDomain = "INCIDENT", StandaloneTaskDomain = "STANDALONE_TASK";
    public static readonly string[] Priorities = ["LOW", "MEDIUM", "HIGH", "CRITICAL"];

    /// <summary>Escalation actions: one row per milestone and target role; condition_json is {"sla_milestone":"WARNING"|"BREACH"}.</summary>
    public const string WarningEscalation = "SLA_WARNING_NOTIFY", BreachEscalation = "SLA_BREACH_ESCALATE";
    public const string Warning = "WARNING", Breach = "BREACH";
    public static readonly string[] EscalationRoles = ["OPERATOR", "MANAGER"];
    public static readonly string[] DefaultWarningRoles = ["OPERATOR"];
    public static readonly string[] DefaultBreachRoles = ["OPERATOR", "MANAGER"];

    public static bool IsPriority(string? value) => value is not null && Priorities.Contains(value);

    public static string Priority(string? value)
    {
        var priority = value?.Trim().ToUpperInvariant() ?? "";
        Rules.Require(IsPriority(priority), "INVALID_SLA_PRIORITY", "SLA priority must be LOW, MEDIUM, HIGH or CRITICAL.");
        return priority;
    }

    public static void Timing(int responseTargetSec, int resolutionTargetSec, int offerTimeoutSec, int warningPercent)
    {
        Rules.Require(responseTargetSec > 0 && resolutionTargetSec >= responseTargetSec && offerTimeoutSec > 0,
            "INVALID_SLA_TARGETS", "SLA targets must be positive and resolution cannot be earlier than response.");
        Rules.Require(offerTimeoutSec <= resolutionTargetSec, "INVALID_SLA_OFFER_TIMEOUT", "Offer timeout cannot be longer than the resolution target.");
        Rules.Require(warningPercent is > 0 and < 100, "INVALID_SLA_WARNING", "Warning percent must be between 1 and 99.");
    }

    public static string EscalationActionType(string milestone) => milestone == Breach ? BreachEscalation : WarningEscalation;

    public static string MilestoneCondition(string milestone) => JsonSerializer.Serialize(new Dictionary<string, string> { ["sla_milestone"] = milestone });

    /// <summary>Returns WARNING/BREACH for an escalation action condition; null for every other action (alert-level actions included).</summary>
    public static string? Milestone(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 4000) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var properties = doc.RootElement.EnumerateObject().ToArray();
            if (properties.Length != 1 || properties[0].Name != "sla_milestone" || properties[0].Value.ValueKind != JsonValueKind.String) return null;
            var milestone = properties[0].Value.GetString();
            return milestone is Warning or Breach ? milestone : null;
        }
        catch (JsonException) { return null; }
    }

    public static bool IsEscalation(SlaPolicyAction action)
    {
        var milestone = Milestone(action.ConditionJson);
        return milestone is not null && action.ActionType == EscalationActionType(milestone);
    }

    public static string[] Roles(IEnumerable<string>? roles, string field)
    {
        var values = (roles ?? []).Select(r => r?.Trim().ToUpperInvariant() ?? "").Distinct().ToArray();
        Rules.Require(values.Length > 0, "INVALID_SLA_ESCALATION", $"{field} needs at least one role.");
        Rules.Require(values.All(value => EscalationRoles.Contains(value)), "INVALID_SLA_ESCALATION", $"{field} can target only OPERATOR or MANAGER.");
        return values;
    }
}
