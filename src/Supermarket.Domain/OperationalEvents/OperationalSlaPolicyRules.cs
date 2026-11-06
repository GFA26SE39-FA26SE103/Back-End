using System.Text.Json;

namespace Supermarket.Domain;

/// <summary>EventType selects the policy; alert level selects its action, not its SLA priority.</summary>
public static class OperationalSlaPolicyRules
{
    public const string NotifyOperator = "NOTIFY_OPERATOR", CreateTask = "CREATE_OPERATIONAL_TASK";

    public static string? AlertLevel(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 4000) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            var properties = doc.RootElement.EnumerateObject().ToArray();
            if (properties.Length != 1 || properties[0].Name != "alert_level" || properties[0].Value.ValueKind != JsonValueKind.String) return null;
            var level = properties[0].Value.GetString();
            return level is "WARNING" or "CRITICAL" ? level : null;
        }
        catch (JsonException) { return null; }
    }

    public static bool Valid(SlaPolicy policy) => policy.PolicyId != Guid.Empty && policy.Version > 0
        && policy.ProblemDomain == "OPERATIONAL_EVENT" && policy.EventTypeId is not null && policy.IncidentTypeId is null
        && policy.PriorityCode is "LOW" or "MEDIUM" or "HIGH" or "CRITICAL"
        && policy.ResponseTargetSec > 0 && policy.ResolutionTargetSec >= policy.ResponseTargetSec
        && policy.OfferTimeoutSec > 0 && policy.WarningPercent is > 0 and < 100;
}
