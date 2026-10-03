using System.Text.Json;
namespace Supermarket.Domain;

public sealed class IncidentType : TrackedEntity
{
    public Guid IncidentTypeId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string SourceType { get; set; } = "";
    public string? MeasurementType { get; set; }
    public bool RequiresBeforePhoto { get; set; }
    public string DefaultSeverity { get; set; } = "WARNING";
    public string Status { get; set; } = "ACTIVE";
}
public sealed class MonitoringRule : TrackedEntity
{
    public Guid RuleId { get; set; }
    public Guid ConfigId { get; set; }
    public Guid IncidentTypeId { get; set; }
    public decimal WarningThreshold { get; set; }
    public decimal CriticalThreshold { get; set; }
    public string ThresholdUnit { get; set; } = "";
    public int SustainSec { get; set; } = 30;
    public int CooldownSec { get; set; } = 300;
    public string? ParametersJson { get; set; }
    public bool Enabled { get; set; } = true;
}
public sealed record MeasurementDefinition(string Unit, decimal Warning, decimal Critical);
public static class MonitoringPolicy
{
    // Agreed measurement/unit pairs. Checkout remains pending counter/composite design.
    public static MeasurementDefinition? Definition(IncidentType type) => (type.Code, type.MeasurementType) switch
    {
        ("LONG_QUEUE", "QUEUE_LENGTH") => new("PEOPLE", 3, 5),
        ("EXCESSIVE_WAITING_TIME", "WAITING_TIME") => new("MINUTES", 4, 8),
        ("OVERCROWDING_CONGESTION", "CROWD_DENSITY") => new("PEOPLE_PER_M2", 2, 3),
        _ => null
    };
    public static void Confidence(decimal value)
    {
        Rules.Confidence(value);
        Rules.Require(decimal.Round(value, 4) == value, "INVALID_CONFIDENCE", "Confidence must use at most four decimal places.");
    }
    public static void Validate(MonitoringRule rule, IncidentType type)
    {
        Rules.Require(type.SourceType == "AI_DETECTED", "INCIDENT_TYPE_NOT_AI", "Monitoring rules require an AI-detected incident type.");
        Rules.Require(!rule.Enabled || type.Status == "ACTIVE", "INCIDENT_TYPE_INACTIVE", "Enabled rules require an active incident type.");
        Rules.Require(rule.WarningThreshold >= 0 && rule.CriticalThreshold > rule.WarningThreshold
            && rule.CriticalThreshold <= 99999999999999.9999m && decimal.Round(rule.WarningThreshold, 4) == rule.WarningThreshold
            && decimal.Round(rule.CriticalThreshold, 4) == rule.CriticalThreshold,
            "INVALID_THRESHOLDS", "Use 0 <= warning < critical, at most four decimal places, within decimal(18,4).");
        rule.ThresholdUnit = Rules.Text(rule.ThresholdUnit, 30, "Threshold unit");
        Rules.Require(rule.SustainSec >= 0 && rule.CooldownSec >= 0, "INVALID_RULE_TIMING", "Sustain and cooldown must be non-negative seconds.");
        var definition = Definition(type);
        Rules.Require(!rule.Enabled || definition is not null, "RULE_UNSUPPORTED", "This measurement is not ready; keep its rule disabled in Draft.");
        if (definition is not null)
        {
            Rules.Require(rule.ThresholdUnit == definition.Unit, "INVALID_RULE_UNIT", $"The measurement requires {definition.Unit}.");
            if (definition.Unit == "PEOPLE") Rules.Require(decimal.Truncate(rule.WarningThreshold) == rule.WarningThreshold && decimal.Truncate(rule.CriticalThreshold) == rule.CriticalThreshold,
                "INVALID_THRESHOLDS", "Queue thresholds must be whole people counts.");
        }
        Rules.Optional(rule.ParametersJson, 16000, "Rule parameters");
        if (rule.ParametersJson is not null)
        {
            try
            {
                using var json = JsonDocument.Parse(rule.ParametersJson);
                Rules.Require(json.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array, "INVALID_RULE_PARAMETERS", "Rule parameters must be a JSON object or array.");
            }
            catch (JsonException) { throw new DomainException("INVALID_RULE_PARAMETERS", "Rule parameters must be valid JSON."); }
        }
    }
}
