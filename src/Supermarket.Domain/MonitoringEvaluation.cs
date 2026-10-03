namespace Supermarket.Domain;

public sealed record MonitoringEvaluationSettings(string Mode,string Unit,decimal WarningThreshold,decimal CriticalThreshold,
    int SustainSec,int CooldownSec,bool Enabled);
public sealed record RuleObservation(Guid SessionId,Guid ContinuityId,long Sequence,long SourceElapsedMs,decimal MetricValue,
    string ConfigFingerprint,bool ContinuityBroken);
public sealed record RuleEvaluationState(Guid? SessionId=null,Guid? ContinuityId=null,long Sequence=0,long SourceElapsedMs=0,
    string? ConfigFingerprint=null,long? WarningStartMs=null,long? CriticalStartMs=null);
public sealed record RuleEvaluationResult(RuleEvaluationState NextState,string? EligibleSeverity,long WarningProgressMs,long CriticalProgressMs,
    string Reason,bool Accepted=true);

public static class MonitoringRuleEvaluator
{
    public static RuleEvaluationResult Evaluate(MonitoringEvaluationSettings settings,RuleEvaluationState state,RuleObservation observation)
    {
        if(!settings.Enabled) return new(new(),null,0,0,"RULE_DISABLED",false);
        if(settings.Mode is not ("PEOPLE_COUNT" or "QUEUE_LENGTH") || settings.Unit!="PEOPLE") return new(new(),null,0,0,"RULE_RUNTIME_UNSUPPORTED",false);
        if(observation.MetricValue<0 || decimal.Truncate(observation.MetricValue)!=observation.MetricValue || observation.Sequence<1
           || observation.SourceElapsedMs<0 || observation.SessionId==Guid.Empty || observation.ContinuityId==Guid.Empty)
            return new(new(),null,0,0,"MEASUREMENT_INVALID",false);
        if(settings.SustainSec<0 || settings.CooldownSec<0 || settings.WarningThreshold<0 || settings.CriticalThreshold<=settings.WarningThreshold)
            return new(new(),null,0,0,"RULE_INVALID",false);
        var matching=state.SessionId==observation.SessionId && state.ContinuityId==observation.ContinuityId && state.ConfigFingerprint==observation.ConfigFingerprint;
        if(matching && state.Sequence==observation.Sequence)
            return new(state,null,0,0,"DUPLICATE",false);
        var broken=observation.ContinuityBroken || !matching || observation.Sequence!=state.Sequence+1 || observation.SourceElapsedMs<state.SourceElapsedMs;
        var warning=observation.MetricValue>=settings.WarningThreshold ? (!broken?state.WarningStartMs:null)??observation.SourceElapsedMs : (long?)null;
        var critical=observation.MetricValue>=settings.CriticalThreshold ? (!broken?state.CriticalStartMs:null)??observation.SourceElapsedMs : (long?)null;
        var warningProgress=warning is null?0:observation.SourceElapsedMs-warning.Value;
        var criticalProgress=critical is null?0:observation.SourceElapsedMs-critical.Value;
        var sustain=settings.SustainSec*1000L;
        var severity=critical is not null && criticalProgress>=sustain ? "CRITICAL" : warning is not null && warningProgress>=sustain ? "WARNING" : null;
        var next=new RuleEvaluationState(observation.SessionId,observation.ContinuityId,observation.Sequence,observation.SourceElapsedMs,
            observation.ConfigFingerprint,warning,critical);
        return new(next,severity,warningProgress,criticalProgress,severity is not null?"THRESHOLD_SUSTAINED":warning is not null?"SUSTAIN_PENDING":"NORMAL");
    }
}

public sealed record IncidentDecision(string Action,string? Severity,decimal CooldownRemainingSec,bool Escalated=false);
public static class IncidentPolicy
{
    public static IncidentDecision Decide(Incident? existingIncident,DateTime? latestEndedAt,string? eligibleSeverity,
        MonitoringEvaluationSettings settings,DateTime utcNow)
    {
        if(existingIncident is { ClosedAt:null })
        {
            var escalate=eligibleSeverity=="CRITICAL" && existingIncident.Severity!="CRITICAL"
                || eligibleSeverity=="WARNING" && existingIncident.Severity=="INFO";
            return new("UPDATE",escalate?eligibleSeverity:existingIncident.Severity,0,escalate);
        }
        var remaining=latestEndedAt is null?0m:Math.Max(0m,settings.CooldownSec-(decimal)(utcNow-latestEndedAt.Value).TotalSeconds);
        if(eligibleSeverity is null) return new("OBSERVE",null,remaining);
        if(remaining>0) return new("SUPPRESSED",null,remaining);
        return new("CREATE",eligibleSeverity,0);
    }
}
