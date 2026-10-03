using Supermarket.Domain;
using Xunit;
namespace Supermarket.Tests;
public sealed class MonitoringEvaluationTests
{
    private readonly Guid session=Guid.NewGuid(),epoch=Guid.NewGuid();
    private static MonitoringEvaluationSettings Settings=>new("PEOPLE_COUNT","PEOPLE",1,2,1,10,true);
    private RuleObservation At(long seq,long time,decimal metric,bool gap=false)=>new(session,epoch,seq,time,metric,"f",gap);
    [Fact]
    public void WarningAndCriticalHaveIndependentContinuousSustainTimers()
    {
        var state=new RuleEvaluationState();
        var first=MonitoringRuleEvaluator.Evaluate(Settings,state,At(1,0,1)); Assert.Null(first.EligibleSeverity);
        var almost=MonitoringRuleEvaluator.Evaluate(Settings,first.NextState,At(2,999,2)); Assert.Null(almost.EligibleSeverity);
        var warning=MonitoringRuleEvaluator.Evaluate(Settings,almost.NextState,At(3,1000,2)); Assert.Equal("WARNING",warning.EligibleSeverity);
        Assert.Equal(1,warning.CriticalProgressMs);
        var critical=MonitoringRuleEvaluator.Evaluate(Settings,warning.NextState,At(4,1999,2)); Assert.Equal("CRITICAL",critical.EligibleSeverity);
        var drop=MonitoringRuleEvaluator.Evaluate(Settings,critical.NextState,At(5,2000,1)); Assert.Equal("WARNING",drop.EligibleSeverity); Assert.Equal(0,drop.CriticalProgressMs);
        var normal=MonitoringRuleEvaluator.Evaluate(Settings,drop.NextState,At(6,2040,0)); Assert.Null(normal.EligibleSeverity); Assert.Equal(0,normal.WarningProgressMs);
    }
    [Fact]
    public void DuplicateGapVersionAndDisabledNeverInheritSustain()
    {
        var first=MonitoringRuleEvaluator.Evaluate(Settings,new(),At(1,0,2));
        var duplicate=MonitoringRuleEvaluator.Evaluate(Settings,first.NextState,At(1,10000,2));
        Assert.Equal(first.NextState,duplicate.NextState); Assert.Null(duplicate.EligibleSeverity);
        foreach(var observation in new[] { At(2,1000,2,true), At(3,1000,2),At(2,1000,2) with { ConfigFingerprint="new" },At(2,1000,2) with { SessionId=Guid.NewGuid() } })
            Assert.Null(MonitoringRuleEvaluator.Evaluate(Settings,first.NextState,observation).EligibleSeverity);
        Assert.Null(MonitoringRuleEvaluator.Evaluate(Settings with { Enabled=false },first.NextState,At(2,10000,2)).EligibleSeverity);
        Assert.Equal("CRITICAL",MonitoringRuleEvaluator.Evaluate(Settings with { SustainSec=0 },new(),At(1,0,2)).EligibleSeverity);
        Assert.Null(MonitoringRuleEvaluator.Evaluate(Settings with { Mode="CROWD_DENSITY" },new(),At(1,0,2)).EligibleSeverity);
    }
    [Fact]
    public void SourceTimeNotWallTimeAndNegativeFractionalMeasurementRejected()
    {
        var first=MonitoringRuleEvaluator.Evaluate(Settings with { SustainSec=10 },new(),At(1,0,2));
        Assert.Null(MonitoringRuleEvaluator.Evaluate(Settings with { SustainSec=10 },first.NextState,At(2,1000,2)).EligibleSeverity);
        Assert.Equal("MEASUREMENT_INVALID",MonitoringRuleEvaluator.Evaluate(Settings,new(),At(1,0,-1)).Reason);
        Assert.Equal("MEASUREMENT_INVALID",MonitoringRuleEvaluator.Evaluate(Settings,new(),At(1,0,1.5m)).Reason);
    }
    [Fact]
    public void CooldownStartsAtClosureAndOpenIncidentNeverDowngradesOrCloses()
    {
        var now=new DateTime(2026,10,3,0,0,0,DateTimeKind.Utc);
        Assert.Equal("SUPPRESSED",IncidentPolicy.Decide(null,now.AddSeconds(-9),"WARNING",Settings,now).Action);
        Assert.Equal("CREATE",IncidentPolicy.Decide(null,now.AddSeconds(-10),"WARNING",Settings,now).Action);
        var open=new Incident { Severity="CRITICAL",Status="DETECTED",CreatedAt=now.AddDays(-1) };
        var below=IncidentPolicy.Decide(open,null,null,Settings,now);
        Assert.Equal("UPDATE",below.Action); Assert.Equal("CRITICAL",below.Severity); Assert.Null(open.ClosedAt); Assert.Equal("DETECTED",open.Status);
        open.Severity="WARNING";
        Assert.Equal("CRITICAL",IncidentPolicy.Decide(open,null,"CRITICAL",Settings,now).Severity);
    }
}
