using System.Text.Json;
using Supermarket.Domain.CheckoutCounters;
namespace Supermarket.Domain;

public sealed class IncidentType : TrackedEntity
{
    public Guid IncidentTypeId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool RequiresBeforePhoto { get; set; }
    public string Status { get; set; } = "ACTIVE";
}
public sealed class MonitoringRule : TrackedEntity
{
    public Guid RuleId { get; set; }
    public Guid ConfigId { get; set; }
    public Guid? IncidentTypeId { get; set; }
    public Guid? EventTypeId { get; set; }
    public string OutcomeType { get; set; } = "OPERATIONAL_EVENT";
    public decimal WarningThreshold { get; set; }
    public decimal CriticalThreshold { get; set; }
    public string ThresholdUnit { get; set; } = "";
    public int SustainSec { get; set; } = 30;
    public int CooldownSec { get; set; } = 300;
    public int ClearDurationSec { get; set; } = 60;
    public string? ParametersJson { get; set; }
    public bool Enabled { get; set; } = true;
}
public sealed record MeasurementDefinition(string Unit, decimal Warning, decimal Critical);
public sealed record RuntimeMeasurementDefinition(string Mode, string Unit, bool Supported, string? Reason);
public static class MonitoringPolicy
{
    public static string? OperationalEventCode(string incidentCode) => incidentCode switch
    {
        "LONG_QUEUE" => "LONG_QUEUE",
        "EXCESSIVE_WAITING_TIME" => "EXCESSIVE_WAIT",
        "OVERCROWDING_CONGESTION" => "CONGESTION",
        "CHECKOUT_CAPACITY_ISSUE" => "CHECKOUT_CAPACITY",
        _ => null
    };
    public static string? OperationalEventCode(IncidentType type) => OperationalEventCode(type.Code);
    public static int RecoverySeconds(MonitoringRule rule) => rule.ClearDurationSec;
    public static RuntimeMeasurementDefinition RuntimeDefinition(MonitoringRule rule, OperationalEventType type)
    {
        if (type.Code=="CHECKOUT_CAPACITY")
            return rule.ThresholdUnit=="PEOPLE_PER_STAFFED_COUNTER"
                ? new("CHECKOUT_CAPACITY","PEOPLE_PER_STAFFED_COUNTER",true,null)
                : new("CHECKOUT_CAPACITY",rule.ThresholdUnit,false,"Checkout capacity requires configured PEOPLE_PER_STAFFED_COUNTER thresholds.");
        if (type.Code=="EXCESSIVE_WAIT" && type.MeasurementType=="WAITING_TIME" && rule.ThresholdUnit=="MINUTES")
            return new("WAITING_TIME","MINUTES",true,null);
        if (type.Code=="LONG_QUEUE" && type.MeasurementType=="QUEUE_LENGTH" && rule.ThresholdUnit=="PEOPLE")
            return new("QUEUE_LENGTH","PEOPLE",true,null);
        if (type.Code=="CONGESTION" && type.MeasurementType is "PEOPLE_COUNT" or "CROWD_DENSITY")
            return rule.ThresholdUnit=="PEOPLE"
                ? new("PEOPLE_COUNT","PEOPLE",true,null)
                : new("PEOPLE_COUNT",rule.ThresholdUnit,false,"Overcrowding uses the current number of people inside the ROI and requires PEOPLE thresholds.");
        return new(type.MeasurementType??"UNSUPPORTED",rule.ThresholdUnit,false,"This measurement has no runtime yet; keep its rule disabled in Draft.");
    }
    public static void ValidateForActivation(MonitoringRule rule, OperationalEventType type)
    {
        Validate(rule,type);
        var runtime=RuntimeDefinition(rule,type);
        Rules.Require(!rule.Enabled || runtime.Supported,"RULE_RUNTIME_UNSUPPORTED",runtime.Reason??"Measurement runtime is unsupported.");
    }
    // Agreed defaults only. Checkout thresholds must be explicitly configured.
    public static MeasurementDefinition? Definition(OperationalEventType type) => (type.Code, type.MeasurementType) switch
    {
        ("LONG_QUEUE", "QUEUE_LENGTH") => new("PEOPLE", 3, 5),
        ("EXCESSIVE_WAIT", "WAITING_TIME") => new("MINUTES", 4, 8),
        ("CONGESTION", "PEOPLE_COUNT") => new("PEOPLE", 3, 5),
        ("CONGESTION", "CROWD_DENSITY") => new("PEOPLE", 3, 5), // Legacy catalog compatibility until V19 is applied.
        _ => null
    };
    public static void Confidence(decimal value)
    {
        Rules.Confidence(value);
        Rules.Require(decimal.Round(value, 4) == value, "INVALID_CONFIDENCE", "Confidence must use at most four decimal places.");
    }
    public static void Validate(MonitoringRule rule, OperationalEventType type)
    {
        Rules.Require(rule.OutcomeType == "OPERATIONAL_EVENT" && rule.EventTypeId is not null && rule.IncidentTypeId is null,
            "INVALID_RULE_TARGET", "Operational-event rules require only an operational event type.");
        Rules.Require(!rule.Enabled || type.Status == "ACTIVE", "OPERATIONAL_EVENT_TYPE_INACTIVE", "Enabled rules require an active operational event type.");
        Rules.Require(rule.WarningThreshold >= 0 && rule.CriticalThreshold > rule.WarningThreshold
            && rule.CriticalThreshold <= 99999999999999.9999m && decimal.Round(rule.WarningThreshold, 4) == rule.WarningThreshold
            && decimal.Round(rule.CriticalThreshold, 4) == rule.CriticalThreshold,
            "INVALID_THRESHOLDS", "Use 0 <= warning < critical, at most four decimal places, within decimal(18,4).");
        rule.ThresholdUnit = Rules.Text(rule.ThresholdUnit, 30, "Threshold unit");
        Rules.Require(rule.SustainSec >= 0 && rule.CooldownSec >= 0 && rule.ClearDurationSec is >= 1 and <= 86400,
            "INVALID_RULE_TIMING", "Sustain and cooldown must be non-negative; clear duration must be from 1 to 86400 seconds.");
        var checkout = type.Code == "CHECKOUT_CAPACITY";
        if (checkout && rule.Enabled)
        {
            Rules.Require(rule.WarningThreshold > 0, "INVALID_THRESHOLDS", "Checkout thresholds require 0 < warning < critical.");
            Rules.Require(rule.ThresholdUnit == "PEOPLE_PER_STAFFED_COUNTER", "INVALID_RULE_UNIT", "Checkout capacity requires PEOPLE_PER_STAFFED_COUNTER thresholds.");
            _ = CheckoutRuleParameters.Parse(rule.ParametersJson);
        }
        else
        {
            var definition = Definition(type);
            Rules.Require(!rule.Enabled || definition is not null, "RULE_UNSUPPORTED", "This measurement is not ready; keep its rule disabled in Draft.");
            if (definition is not null && rule.Enabled)
            {
                var runtime=RuntimeDefinition(rule,type);
                var unit=runtime.Supported ? runtime.Unit : definition.Unit;
                Rules.Require(rule.ThresholdUnit == unit, "INVALID_RULE_UNIT", $"The measurement requires {unit} and an explicit matching measurement mode.");
                if (unit == "PEOPLE") Rules.Require(decimal.Truncate(rule.WarningThreshold) == rule.WarningThreshold && decimal.Truncate(rule.CriticalThreshold) == rule.CriticalThreshold,
                    "INVALID_THRESHOLDS", "Thresholds must be whole people counts.");
            }
        }
        Rules.Optional(rule.ParametersJson, 16000, "Rule parameters");
        if (rule.ParametersJson is not null && !(checkout && !rule.Enabled && string.IsNullOrWhiteSpace(rule.ParametersJson)))
        {
            try
            {
                using var json = JsonDocument.Parse(rule.ParametersJson);
                Rules.Require(json.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array, "INVALID_RULE_PARAMETERS", "Rule parameters must be a JSON object or array.");
            }
            catch (JsonException) { throw new DomainException("INVALID_RULE_PARAMETERS", "Rule parameters must be valid JSON."); }
        }
        RuleHysteresis.Validate(rule);
        _=RecoverySeconds(rule);
    }
}
