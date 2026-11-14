using Supermarket.Application;
using Supermarket.Domain;
using Xunit;

namespace Supermarket.Tests;

/// <summary>Dual (hysteresis) thresholds: enter at ≥ threshold, hold until &lt; exit, so noise cannot flip levels or clear/re-create events.</summary>
public sealed class RuleHysteresisTests
{
    private readonly Guid session = Guid.NewGuid(), epoch = Guid.NewGuid();
    private static readonly MonitoringEvaluationSettings Dual = new("QUEUE_LENGTH", "PEOPLE", 10, 20, 0, 0, true, 2, 8, 18);
    private RuleObservation At(long seq, long time, decimal metric) => new(session, epoch, seq, time, metric, "f", false);

    [Fact]
    public void LevelsAreHeldInsideTheDeadBandAndOnlyLeftBelowTheExitThreshold()
    {
        var expected = new (decimal Value, string? Level, bool Recovering)[]
        {
            (21, "CRITICAL", false), // enters CRITICAL (>= 20)
            (19, "CRITICAL", false), // 18 <= 19 < 20: held, no flip to WARNING
            (21, "CRITICAL", false),
            (17, "WARNING", false),  // below the critical exit (18)
            (19, "WARNING", false),  // not re-entered: still below the critical enter (20)
            (9, "WARNING", false),   // 8 <= 9 < 10: WARNING held, no recovery timer
            (7, null, true),         // below the warning exit: recovery starts
            (9, null, true),         // not re-entered (below 10): recovery continues
            (10, "WARNING", false),  // re-entered at the enter threshold
        };
        var state = new RuleEvaluationState();
        for (var i = 0; i < expected.Length; i++)
        {
            var result = MonitoringRuleEvaluator.Evaluate(Dual, state, At(i + 1, i * 1000, expected[i].Value));
            Assert.True(result.Accepted, result.Reason);
            Assert.Equal(expected[i].Level, result.EligibleSeverity);
            Assert.Equal(expected[i].Recovering, result.NextState.RecoveryStartMs is not null);
            state = result.NextState;
        }
    }

    [Fact]
    public void RecoveryClearsOnlyAfterTheValueStaysBelowTheWarningExit()
    {
        var state = MonitoringRuleEvaluator.Evaluate(Dual, new(), At(1, 0, 12)).NextState;
        var band = MonitoringRuleEvaluator.Evaluate(Dual, state, At(2, 1000, 9));
        Assert.False(band.ShouldClear);
        var below = MonitoringRuleEvaluator.Evaluate(Dual, band.NextState, At(3, 2000, 5));
        var stillBelow = MonitoringRuleEvaluator.Evaluate(Dual, below.NextState, At(4, 4000, 6));
        Assert.True(stillBelow.ShouldClear);
    }

    [Fact]
    public void MissingExitsKeepSingleThresholdBehaviourAndInvalidExitsAreRejected()
    {
        var single = Dual with { WarningExitThreshold = null, CriticalExitThreshold = null };
        var critical = MonitoringRuleEvaluator.Evaluate(single, new(), At(1, 0, 20));
        Assert.Equal("CRITICAL", critical.EligibleSeverity);
        Assert.Equal("WARNING", MonitoringRuleEvaluator.Evaluate(single, critical.NextState, At(2, 1000, 19)).EligibleSeverity);
        foreach (var invalid in new[] { Dual with { WarningExitThreshold = 11 }, Dual with { WarningExitThreshold = -1 },
                     Dual with { CriticalExitThreshold = 21 }, Dual with { CriticalExitThreshold = 10 } })
            Assert.Equal("RULE_INVALID", MonitoringRuleEvaluator.Evaluate(invalid, new(), At(1, 0, 12)).Reason);
    }

    [Fact]
    public void ExitsAreMergedIntoParametersWithoutLosingOtherKeysAndValidated()
    {
        Assert.Null(RuleHysteresis.Merge(null, null, null));
        const string checkout = "{\"minQueue\":2,\"noStaffedAlertLevel\":\"CRITICAL\"}";
        Assert.Equal(checkout, RuleHysteresis.Merge(checkout, null, null));
        var merged = RuleHysteresis.Merge(checkout, 8, 18);
        Assert.Equal(((decimal?)8m, (decimal?)18m), RuleHysteresis.Read(merged));
        Assert.Contains("minQueue", merged);
        var removed = RuleHysteresis.Merge(merged, null, null);
        Assert.Equal(((decimal?)null, (decimal?)null), RuleHysteresis.Read(removed));
        Assert.Contains("noStaffedAlertLevel", removed);
        Assert.Equal("INVALID_RULE_PARAMETERS", Assert.Throws<DomainException>(() => RuleHysteresis.Merge("[1]", 8, null)).Code);

        MonitoringRule Rule(string json, string unit = "PEOPLE") => new() { WarningThreshold = 10, CriticalThreshold = 20, ThresholdUnit = unit, ParametersJson = json };
        RuleHysteresis.Validate(Rule("{\"warningExitThreshold\":8,\"criticalExitThreshold\":18}"));
        RuleHysteresis.Validate(Rule("{\"warningExitThreshold\":7.5}", "MINUTES"));
        foreach (var json in new[] { "{\"warningExitThreshold\":11}", "{\"criticalExitThreshold\":10}", "{\"criticalExitThreshold\":21}",
                     "{\"warningExitThreshold\":7.5}", "{\"warningExitThreshold\":\"8\"}" })
            Assert.ThrowsAny<DomainException>(() => RuleHysteresis.Validate(Rule(json)));
    }

    [Fact]
    public async Task SavedRuleStoresExitsAndExposesThemInTheConfigurationAndRuntimeSettings()
    {
        var h = new MonitoringSetupTests.Harness();
        var type = h.AddRule();
        var input = new MonitoringRuleRequest(type.IncidentTypeId, 10, 20, "PEOPLE", 0, 0, 45, WarningExitThreshold: 8, CriticalExitThreshold: 18);
        var saved = await h.Setup.Save(h.Zone.ZoneId, new MonitoringRequest("Dual", .6m, [input], h.Configuration.UpdatedAt), default);
        var view = Assert.Single(saved.Rules);
        Assert.Equal((8m, 18m), (view.WarningExitThreshold!.Value, view.CriticalExitThreshold!.Value));
        var stored = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        var snapshot = new MonitoringRuleSnapshot(stored.RuleId, null, "LONG_QUEUE", "Long queue", "QUEUE_LENGTH", "PEOPLE", 10, 20, 0, 0, ParametersJson: stored.ParametersJson);
        Assert.Equal((8m, 18m), (snapshot.Settings().WarningExit, snapshot.Settings().CriticalExit));
        var runtime = new MonitoringRuleRuntimeView(stored.RuleId, "LONG_QUEUE", "Long queue", "QUEUE_LENGTH", "PEOPLE", 10, 20, 0, 0, 45, 19, 0, 0, 0, "OK").WithExits(snapshot);
        Assert.Equal((8m, 18m), (runtime.WarningExitThreshold!.Value, runtime.CriticalExitThreshold!.Value));

        var invalid = input with { WarningExitThreshold = 12 };
        await Assert.ThrowsAnyAsync<Exception>(() => h.Setup.Save(h.Zone.ZoneId, new MonitoringRequest("Bad", .6m, [invalid], saved.UpdatedAt), default));
    }
}
