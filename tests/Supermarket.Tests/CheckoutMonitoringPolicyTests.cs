using System.Globalization;
using Supermarket.Domain;
using Supermarket.Domain.CheckoutCounters;
using Xunit;

namespace Supermarket.Tests;

public sealed class CheckoutMonitoringPolicyTests
{
    [Fact]
    public void WaitingTimeUsesConfiguredMinutesInsteadOfPeople()
    {
        var type = new OperationalEventType { Code = "EXCESSIVE_WAIT", MeasurementType = "WAITING_TIME", Status = "ACTIVE" };
        var rule = new MonitoringRule { EventTypeId = Guid.NewGuid(), ThresholdUnit = "MINUTES",
            WarningThreshold = 4, CriticalThreshold = 8, Enabled = true };
        MonitoringPolicy.ValidateForActivation(rule, type);
        Assert.Equal("WAITING_TIME", MonitoringPolicy.RuntimeDefinition(rule, type).Mode);
    }
    private const string RatioUnit = "PEOPLE_PER_STAFFED_COUNTER";
    private const string ValidParameters = "{\"minQueue\":3,\"noStaffedAlertLevel\":\"WARNING\"}";
    private readonly Guid session = Guid.NewGuid(), continuity = Guid.NewGuid();
    private static OperationalEventType Type => new() { EventTypeId = Guid.NewGuid(), Code = "CHECKOUT_CAPACITY" };
    private static MonitoringRule Rule => new()
    {
        EventTypeId = Guid.NewGuid(), WarningThreshold = 3.2501m, CriticalThreshold = 5.5002m,
        ThresholdUnit = RatioUnit, ParametersJson = ValidParameters, SustainSec = 15, ClearDurationSec = 15
    };
    private static MonitoringEvaluationSettings Settings => new("CHECKOUT_CAPACITY", RatioUnit, 3.2501m, 5.5002m, 15, 30, true, 15);
    private RuleObservation At(long sequence, long sourceTime, decimal metric) =>
        new(session, continuity, sequence, sourceTime, metric, "checkout-config", false);

    [Theory]
    [InlineData("WARNING")]
    [InlineData("CRITICAL")]
    public void ParsesExplicitQueueMinimumAndNoStaffedAlertLevel(string level)
    {
        var parameters = CheckoutRuleParameters.Parse($"{{\"minQueue\":1,\"noStaffedAlertLevel\":\"{level}\"}}");
        Assert.Equal(new CheckoutRuleParameters(1, level), parameters);
    }

    [Fact]
    public void ParsesLargestIntegerQueueMinimumWithoutInventingLimits()
    {
        Assert.Equal(int.MaxValue, CheckoutRuleParameters.Parse(
            "{\"minQueue\":2147483647,\"noStaffedAlertLevel\":\"WARNING\"}").MinQueue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("{invalid")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("3")]
    [InlineData("\"parameters\"")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"noStaffedAlertLevel\":\"WARNING\"}")]
    [InlineData("{\"minQueue\":3}")]
    [InlineData("{\"minQueue\":0,\"noStaffedAlertLevel\":\"WARNING\"}")]
    [InlineData("{\"minQueue\":-1,\"noStaffedAlertLevel\":\"WARNING\"}")]
    [InlineData("{\"minQueue\":1.5,\"noStaffedAlertLevel\":\"WARNING\"}")]
    [InlineData("{\"minQueue\":2147483648,\"noStaffedAlertLevel\":\"WARNING\"}")]
    [InlineData("{\"minQueue\":\"3\",\"noStaffedAlertLevel\":\"WARNING\"}")]
    [InlineData("{\"minQueue\":null,\"noStaffedAlertLevel\":\"WARNING\"}")]
    [InlineData("{\"minQueue\":true,\"noStaffedAlertLevel\":\"WARNING\"}")]
    [InlineData("{\"minQueue\":3,\"noStaffedAlertLevel\":null}")]
    [InlineData("{\"minQueue\":3,\"noStaffedAlertLevel\":1}")]
    [InlineData("{\"minQueue\":3,\"noStaffedAlertLevel\":\"INFO\"}")]
    [InlineData("{\"minQueue\":3,\"noStaffedAlertLevel\":\"warning\"}")]
    [InlineData("{\"minQueue\":3,\"noStaffedAlertLevel\":\" CRITICAL \"}")]
    public void RejectsInvalidCheckoutParametersWithStableDomainCode(string? json)
    {
        Assert.Equal("INVALID_CHECKOUT_PARAMETERS", Assert.Throws<DomainException>(() => CheckoutRuleParameters.Parse(json)).Code);
    }

    [Fact]
    public void RejectsOversizedParametersBeforeParsing()
    {
        Assert.Equal("INVALID_CHECKOUT_PARAMETERS", Assert.Throws<DomainException>(() =>
            CheckoutRuleParameters.Parse(ValidParameters + new string(' ', 16000))).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("LEGACY_COMPOSITE")]
    [InlineData("CHECKOUT_CAPACITY")]
    public void ActivatesConfiguredRatioByEventCodeWithoutAssumingMeasurementType(string measurementType)
    {
        var type = Type;
        type.MeasurementType = measurementType;
        var rule = Rule;
        rule.EventTypeId = type.EventTypeId;

        MonitoringPolicy.ValidateForActivation(rule, type);

        Assert.Equal(new RuntimeMeasurementDefinition("CHECKOUT_CAPACITY", RatioUnit, true, null),
            MonitoringPolicy.RuntimeDefinition(rule, type));
        Assert.Equal(3.2501m, rule.WarningThreshold);
        Assert.Equal(5.5002m, rule.CriticalThreshold);
        Assert.Null(MonitoringPolicy.Definition(type));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{invalid")]
    [InlineData("{\"minQueue\":-1,\"noStaffedAlertLevel\":\"WARNING\"}")]
    [InlineData("{\"minQueue\":3}")]
    public void EnabledCheckoutRequiresExplicitValidParameters(string? parameters)
    {
        var rule = Rule;
        rule.ParametersJson = parameters;
        Assert.Equal("INVALID_CHECKOUT_PARAMETERS", Assert.Throws<DomainException>(() => MonitoringPolicy.Validate(rule, Type)).Code);
    }

    [Theory]
    [InlineData("0", "5.5")]
    [InlineData("-1", "5.5")]
    [InlineData("3.25", "3.25")]
    [InlineData("3.25", "3")]
    [InlineData("3.25001", "5.5")]
    [InlineData("3.25", "5.50001")]
    [InlineData("3.25", "100000000000000")]
    public void CheckoutThresholdsMustBePositiveOrderedAndFitFourDecimalStorage(string warning, string critical)
    {
        var rule = Rule;
        rule.WarningThreshold = decimal.Parse(warning, CultureInfo.InvariantCulture);
        rule.CriticalThreshold = decimal.Parse(critical, CultureInfo.InvariantCulture);
        Assert.Equal("INVALID_THRESHOLDS", Assert.Throws<DomainException>(() => MonitoringPolicy.Validate(rule, Type)).Code);
    }

    [Theory]
    [InlineData("PEOPLE")]
    [InlineData("PEOPLE_PER_M2")]
    [InlineData("PENDING")]
    public void EnabledCheckoutRejectsNonRatioUnits(string unit)
    {
        var rule = Rule;
        rule.ThresholdUnit = unit;
        Assert.Equal("INVALID_RULE_UNIT", Assert.Throws<DomainException>(() => MonitoringPolicy.Validate(rule, Type)).Code);
        Assert.False(MonitoringPolicy.RuntimeDefinition(rule, Type).Supported);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    public void DisabledCheckoutDraftRetainsLegacyPlaceholdersAndHasNoDefaultThresholds(string? parameters)
    {
        var rule = Rule;
        rule.Enabled = false;
        rule.ThresholdUnit = "PENDING";
        rule.WarningThreshold = 0;
        rule.CriticalThreshold = 1;
        rule.ParametersJson = parameters;

        MonitoringPolicy.ValidateForActivation(rule, Type);

        Assert.Null(MonitoringPolicy.Definition(Type));
        Assert.False(MonitoringPolicy.RuntimeDefinition(rule, Type).Supported);
        Assert.Equal(parameters, rule.ParametersJson);
        Assert.Equal(0m, rule.WarningThreshold);
        Assert.Equal(1m, rule.CriticalThreshold);
    }

    [Theory]
    [InlineData("3.2501", "WARNING")]
    [InlineData("5.5002", "CRITICAL")]
    public void FractionalRatioSustainsAtFifteenSourceSecondsNotFourteenPointNine(string metric, string expected)
    {
        var value = decimal.Parse(metric, CultureInfo.InvariantCulture);
        var first = MonitoringRuleEvaluator.Evaluate(Settings, new(), At(1, 42000, value));
        Assert.True(first.Accepted);
        Assert.Null(first.EligibleSeverity);
        var almost = MonitoringRuleEvaluator.Evaluate(Settings, first.NextState, At(2, 56900, value));
        Assert.Null(almost.EligibleSeverity);
        Assert.Equal(14900L, almost.WarningProgressMs);
        var sustained = MonitoringRuleEvaluator.Evaluate(Settings, almost.NextState, At(3, 57000, value));
        Assert.Equal(expected, sustained.EligibleSeverity);
        Assert.Equal(15000L, sustained.WarningProgressMs);
    }

    [Fact]
    public void CheckoutCriticalSustainRemainsIndependentFromWarningSustain()
    {
        var warning = MonitoringRuleEvaluator.Evaluate(Settings, new(), At(1, 0, 3.2501m));
        var criticalStart = MonitoringRuleEvaluator.Evaluate(Settings, warning.NextState, At(2, 14900, 5.5002m));
        var warningReady = MonitoringRuleEvaluator.Evaluate(Settings, criticalStart.NextState, At(3, 15000, 5.5002m));
        Assert.Equal("WARNING", warningReady.EligibleSeverity);
        Assert.Equal(100L, warningReady.CriticalProgressMs);
        var criticalReady = MonitoringRuleEvaluator.Evaluate(Settings, warningReady.NextState, At(4, 29900, 5.5002m));
        Assert.Equal("CRITICAL", criticalReady.EligibleSeverity);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.0001")]
    [InlineData("3.25")]
    [InlineData("5.5002")]
    public void CheckoutAcceptsNonnegativeFractionalMetrics(string metric)
    {
        var result = MonitoringRuleEvaluator.Evaluate(Settings, new(), At(1, 0, decimal.Parse(metric, CultureInfo.InvariantCulture)));
        Assert.True(result.Accepted);
    }

    [Fact]
    public void CheckoutRejectsNegativeRatioAndZeroWarningThreshold()
    {
        var negative = MonitoringRuleEvaluator.Evaluate(Settings, new(), At(1, 0, -0.0001m));
        Assert.False(negative.Accepted);
        Assert.Equal("MEASUREMENT_INVALID", negative.Reason);
        var invalidRule = MonitoringRuleEvaluator.Evaluate(Settings with { WarningThreshold = 0 }, new(), At(1, 0, 1.5m));
        Assert.False(invalidRule.Accepted);
        Assert.Equal("RULE_INVALID", invalidRule.Reason);
    }

    [Fact]
    public void CheckoutRecoveryUsesContinuousSourceTimeAndResetsWhenPressureReturns()
    {
        var settings = Settings with { SustainSec = 0 };
        var breached = MonitoringRuleEvaluator.Evaluate(settings, new(), At(1, 0, 5.5002m));
        Assert.Equal("CRITICAL", breached.EligibleSeverity);
        var healthy = MonitoringRuleEvaluator.Evaluate(settings, breached.NextState, At(2, 1000, 0.5m));
        var interrupted = MonitoringRuleEvaluator.Evaluate(settings, healthy.NextState, At(3, 15900, 3.2501m));
        Assert.False(interrupted.ShouldClear);
        Assert.Equal(0L, interrupted.RecoveryProgressMs);
        var restarted = MonitoringRuleEvaluator.Evaluate(settings, interrupted.NextState, At(4, 20000, 0m));
        var almost = MonitoringRuleEvaluator.Evaluate(settings, restarted.NextState, At(5, 34900, 0.5m));
        Assert.False(almost.ShouldClear);
        Assert.Equal(14900L, almost.RecoveryProgressMs);
        var recovered = MonitoringRuleEvaluator.Evaluate(settings, almost.NextState, At(6, 35000, 0m));
        Assert.True(recovered.ShouldClear);
        Assert.Null(recovered.EligibleSeverity);
        Assert.Equal("RECOVERY_SUSTAINED", recovered.Reason);
        Assert.Equal(15000L, recovered.RecoveryProgressMs);
    }

    [Theory]
    [InlineData("gap")]
    [InlineData("sequence")]
    [InlineData("sourceTime")]
    [InlineData("session")]
    [InlineData("continuity")]
    [InlineData("configuration")]
    public void CheckoutContinuityBreakNeverInheritsSustain(string change)
    {
        var first = MonitoringRuleEvaluator.Evaluate(Settings, new(), At(1, 1000, 5.5002m));
        var observation = At(2, 16000, 5.5002m);
        observation = change switch
        {
            "gap" => observation with { ContinuityBroken = true },
            "sequence" => observation with { Sequence = 3 },
            "sourceTime" => observation with { SourceElapsedMs = 999 },
            "session" => observation with { SessionId = Guid.NewGuid() },
            "continuity" => observation with { ContinuityId = Guid.NewGuid() },
            _ => observation with { ConfigFingerprint = "updated-config" }
        };
        var result = MonitoringRuleEvaluator.Evaluate(Settings, first.NextState, observation);
        Assert.True(result.Accepted);
        Assert.Null(result.EligibleSeverity);
        Assert.Equal(0L, result.WarningProgressMs);
        Assert.Equal(0L, result.CriticalProgressMs);
    }

    [Fact]
    public void CheckoutDuplicateAndDisabledObservationsDoNotAdvanceSustain()
    {
        var first = MonitoringRuleEvaluator.Evaluate(Settings, new(), At(1, 0, 5.5002m));
        var duplicate = MonitoringRuleEvaluator.Evaluate(Settings, first.NextState, At(1, 15000, 5.5002m));
        Assert.False(duplicate.Accepted);
        Assert.Equal("DUPLICATE", duplicate.Reason);
        Assert.Equal(first.NextState, duplicate.NextState);
        var disabled = MonitoringRuleEvaluator.Evaluate(Settings with { Enabled = false }, first.NextState, At(2, 15000, 5.5002m));
        Assert.False(disabled.Accepted);
        Assert.Equal("RULE_DISABLED", disabled.Reason);
        Assert.Equal(new RuleEvaluationState(), disabled.NextState);
    }

    [Theory]
    [InlineData("CHECKOUT_CAPACITY", "PEOPLE")]
    [InlineData("CHECKOUT_CAPACITY", "PEOPLE_PER_M2")]
    [InlineData("PEOPLE_COUNT", RatioUnit)]
    [InlineData("QUEUE_LENGTH", RatioUnit)]
    public void RatioUnitCannotBeSubstitutedForGenericCountUnits(string mode, string unit)
    {
        var result = MonitoringRuleEvaluator.Evaluate(Settings with { Mode = mode, Unit = unit }, new(), At(1, 0, 3));
        Assert.False(result.Accepted);
        Assert.Equal("RULE_RUNTIME_UNSUPPORTED", result.Reason);
    }

    [Theory]
    [InlineData("PEOPLE_COUNT")]
    [InlineData("QUEUE_LENGTH")]
    public void GenericCountModesStillRequireWholePeopleAndAllowZeroWarning(string mode)
    {
        var settings = new MonitoringEvaluationSettings(mode, "PEOPLE", 0, 2, 0, 30, true);
        var fraction = MonitoringRuleEvaluator.Evaluate(settings, new(), At(1, 0, 1.5m));
        Assert.False(fraction.Accepted);
        Assert.Equal("MEASUREMENT_INVALID", fraction.Reason);
        var integer = MonitoringRuleEvaluator.Evaluate(settings, new(), At(1, 0, 2));
        Assert.True(integer.Accepted);
        Assert.Equal("CRITICAL", integer.EligibleSeverity);
    }

    [Theory]
    [InlineData("LONG_QUEUE", "QUEUE_LENGTH")]
    [InlineData("CONGESTION", "CROWD_DENSITY")]
    public void GenericPolicyStillAllowsZeroWarningAndRejectsFractionalPeopleThresholds(string code, string measurement)
    {
        var type = new OperationalEventType { EventTypeId = Guid.NewGuid(), Code = code, MeasurementType = measurement };
        var rule = new MonitoringRule
        {
            EventTypeId = type.EventTypeId, ThresholdUnit = "PEOPLE", WarningThreshold = 0, CriticalThreshold = 2,
            ParametersJson = "{\"measurementMode\":\"PEOPLE_COUNT\"}"
        };
        MonitoringPolicy.ValidateForActivation(rule, type);
        Assert.True(MonitoringPolicy.RuntimeDefinition(rule, type).Supported);
        rule.WarningThreshold = 0.5m;
        Assert.Equal("INVALID_THRESHOLDS", Assert.Throws<DomainException>(() => MonitoringPolicy.Validate(rule, type)).Code);
    }
}
