using Supermarket.Domain;
using Xunit;
namespace Supermarket.Tests;

public sealed class MonitoringPolicyTests
{
    [Fact]
    public void PeopleCountIsExplicitAndDoesNotReinterpretDensity()
    {
        var type=new IncidentType { Code="OVERCROWDING_CONGESTION", MeasurementType="CROWD_DENSITY", SourceType="AI_DETECTED" };
        var count=new MonitoringRule { WarningThreshold=1,CriticalThreshold=2,ThresholdUnit="PEOPLE",ParametersJson="{\"custom\":7,\"measurementMode\":\"PEOPLE_COUNT\"}" };
        MonitoringPolicy.Validate(count,type);
        var definition=MonitoringPolicy.RuntimeDefinition(count,type);
        Assert.True(definition.Supported); Assert.Equal("PEOPLE_COUNT",definition.Mode);
        Assert.Equal("PEOPLE",definition.Unit);
        var density=new MonitoringRule { WarningThreshold=2,CriticalThreshold=3,ThresholdUnit="PEOPLE_PER_M2" };
        MonitoringPolicy.Validate(density,type);
        Assert.False(MonitoringPolicy.RuntimeDefinition(density,type).Supported);
        Assert.Equal("PEOPLE_PER_M2",density.ThresholdUnit); Assert.Null(density.ParametersJson);
        count.ParametersJson="[]";
        Assert.Throws<DomainException>(()=>MonitoringPolicy.Validate(count,type));
    }

    [Fact]
    public void UnsupportedLegacyRuleCanBeDisabledInDraft()
    {
        var rule=Rule; rule.Enabled=false; rule.ThresholdUnit="LEGACY";
        var type=Type; type.Code="EXCESSIVE_WAITING_TIME"; type.MeasurementType="WAITING_TIME";
        MonitoringPolicy.Validate(rule,type);
        Assert.False(MonitoringPolicy.RuntimeDefinition(rule,type).Supported);
    }
    private static IncidentType Type => new() { Code = "LONG_QUEUE", SourceType = "AI_DETECTED", MeasurementType = "QUEUE_LENGTH" };
    private static MonitoringRule Rule => new() { WarningThreshold = 3, CriticalThreshold = 5, ThresholdUnit = "PEOPLE" };
    [Theory]
    [InlineData("thresholds", "INVALID_THRESHOLDS")]
    [InlineData("negative", "INVALID_THRESHOLDS")]
    [InlineData("fraction", "INVALID_THRESHOLDS")]
    [InlineData("precision", "INVALID_THRESHOLDS")]
    [InlineData("overflow", "INVALID_THRESHOLDS")]
    [InlineData("unit", "INVALID_RULE_UNIT")]
    [InlineData("timing", "INVALID_RULE_TIMING")]
    [InlineData("json", "INVALID_RULE_PARAMETERS")]
    [InlineData("primitive", "INVALID_RULE_PARAMETERS")]
    [InlineData("staff", "INCIDENT_TYPE_NOT_AI")]
    [InlineData("inactive", "INCIDENT_TYPE_INACTIVE")]
    [InlineData("checkout", "RULE_UNSUPPORTED")]
    public void RejectsInvalidRule(string reason, string code)
    {
        var rule = Rule; var type = Type;
        switch (reason)
        {
            case "thresholds": rule.CriticalThreshold = 3; break;
            case "negative": rule.WarningThreshold = -1; break;
            case "fraction": rule.WarningThreshold = 3.5m; break;
            case "precision": rule.WarningThreshold = 3.00001m; break;
            case "overflow": rule.CriticalThreshold = 100000000000000m; break;
            case "unit": rule.ThresholdUnit = "MINUTES"; break;
            case "timing": rule.CooldownSec = -1; break;
            case "json": rule.ParametersJson = "{invalid"; break;
            case "primitive": rule.ParametersJson = "true"; break;
            case "staff": type.SourceType = "STAFF_REPORTED"; break;
            case "inactive": type.Status = "INACTIVE"; break;
            case "checkout": type.Code = "CHECKOUT_CAPACITY_ISSUE"; type.MeasurementType = "CHECKOUT_CAPACITY"; break;
        }
        Assert.Equal(code, Assert.Throws<DomainException>(() => MonitoringPolicy.Validate(rule, type)).Code);
    }
    [Fact]
    public void AllowsDisabledCheckoutDraftAndExplicitZeroTiming()
    {
        var rule = Rule; var type = Type;
        type.Code = "CHECKOUT_CAPACITY_ISSUE"; type.MeasurementType = "CHECKOUT_CAPACITY";
        rule.Enabled = false; rule.ThresholdUnit = "PENDING"; rule.SustainSec = 0; rule.CooldownSec = 0;
        rule.ParametersJson = "{}";
        MonitoringPolicy.Validate(rule, type);
        Assert.Null(MonitoringPolicy.Definition(type));
    }
    [Theory]
    [InlineData("LONG_QUEUE", "QUEUE_LENGTH", "PEOPLE", 3, 5)]
    [InlineData("EXCESSIVE_WAITING_TIME", "WAITING_TIME", "MINUTES", 4, 8)]
    [InlineData("OVERCROWDING_CONGESTION", "CROWD_DENSITY", "PEOPLE_PER_M2", 2, 3)]
    public void UsesDocumentedConfigurableDefaults(string code, string measurement, string unit, int warning, int critical)
    {
        var definition = MonitoringPolicy.Definition(new IncidentType { Code = code, MeasurementType = measurement })!;
        Assert.Equal(unit, definition.Unit); Assert.Equal(warning, definition.Warning); Assert.Equal(critical, definition.Critical);
        MonitoringPolicy.Validate(new MonitoringRule { WarningThreshold = warning, CriticalThreshold = critical, ThresholdUnit = unit }, new IncidentType { Code = code, MeasurementType = measurement, SourceType = "AI_DETECTED" });
    }
    [Theory]
    [InlineData(-.1)]
    [InlineData(1.1)]
    [InlineData(.50001)]
    public void RejectsInvalidConfidence(double value) => Assert.Equal("INVALID_CONFIDENCE", Assert.Throws<DomainException>(() => MonitoringPolicy.Confidence((decimal)value)).Code);
    [Theory]
    [InlineData(0)]
    [InlineData(.5)]
    [InlineData(1)]
    public void AcceptsConfidenceBoundaries(double value) => MonitoringPolicy.Confidence((decimal)value);
}
