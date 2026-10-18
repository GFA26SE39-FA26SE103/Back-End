using System.Text.Json;
using Supermarket.Domain;
namespace Supermarket.Application;

public sealed class MonitoringIncidentWriter(ISetupStore store,IMonitoringSnapshotReader snapshots,IMonitoringIncidentQueries queries,IClock clock)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);

    public Task<IncidentWriteResult> Apply(IncidentObservationCommand command,CancellationToken ct)
    {
        var evaluation=command.Evaluation;
        if(!evaluation.Accepted) return Task.FromResult(new IncidentWriteResult(false,false,false,null,null,evaluation.Reason));
        if(!command.ShouldSample && evaluation.EligibleSeverity is null && !evaluation.ShouldClear)
            return Task.FromResult(new IncidentWriteResult(true,false,false,null,null,evaluation.Reason));
        var outcome=command.Rule.OutcomeType;
        if(outcome is not ("OPERATIONAL_EVENT" or "INCIDENT"))
            throw new ApplicationException("MONITORING_OUTCOME_INVALID","The monitoring rule outcome type is invalid.",503);
        if(outcome=="OPERATIONAL_EVENT" && (command.Rule.EventTypeId is not { } eventTypeId || eventTypeId==Guid.Empty || string.IsNullOrWhiteSpace(command.Rule.EventTypeCode)))
            throw new ApplicationException("OPERATIONAL_EVENT_TYPE_UNAVAILABLE","The monitoring rule has no operational event type.",503);
        if(outcome=="INCIDENT" && (command.Rule.IncidentTypeId is not { } incidentTypeId || incidentTypeId==Guid.Empty))
            throw new ApplicationException("INCIDENT_TYPE_UNAVAILABLE","The monitoring rule has no incident type.",503);

        return store.Transaction(async()=>
        {
            if(!await snapshots.IsCurrent(command.Snapshot,command.Zone.ZoneId,ct))
                return new IncidentWriteResult(false,true,false,null,null,"CONFIGURATION_CHANGED");

            var rule=command.Rule; var zone=command.Zone; var batch=command.Batch;
            var metric=rule.Mode=="QUEUE_LENGTH"?command.Measurement.QueueCount:command.Measurement.PeopleCount;
            var metadata=Metadata(command,metric);

            if(outcome=="INCIDENT")
            {
                var incidentTypeId=rule.IncidentTypeId!.Value;
                var existing=(await store.List<Incident>(i=>i.ZoneId==zone.ZoneId && i.IncidentTypeId==incidentTypeId && i.ClosedAt==null,ct)).SingleOrDefault();
                if(evaluation.ShouldClear || evaluation.EligibleSeverity is null)
                    return new(true,false,false,existing?.IncidentId,existing?.Severity,evaluation.Reason);

                var latestEnded=existing is null?await queries.LatestEndedAt(zone.ZoneId,incidentTypeId,ct):null;
                var decision=IncidentPolicy.Decide(existing,latestEnded,evaluation.EligibleSeverity,rule.Settings(),clock.UtcNow);
                var incident=existing;
                if(decision.Action=="CREATE")
                {
                    var title=$"{rule.Name} — {zone.ZoneName}";
                    incident=new Incident { IncidentId=Guid.NewGuid(),IncidentTypeId=incidentTypeId,ZoneId=zone.ZoneId,
                        MonitoringRuleId=rule.RuleId,Title=title[..Math.Min(200,title.Length)],Severity=decision.Severity!,
                        Description=$"{rule.Mode}: {metric} {rule.Unit}. AI-detected incident outcome; dispatch is not implemented." };
                    await store.Add(incident,ct);
                }
                else if(incident is not null)
                {
                    incident.MonitoringRuleId??=rule.RuleId;
                    incident.Severity=decision.Severity!;
                    incident.Description=$"{rule.Mode}: {metric} {rule.Unit}. Latest aggregate observation; recovery does not automatically close this incident.";
                    await store.Update(incident,ct);
                }
                var reason=decision.Action=="SUPPRESSED"?"COOLDOWN":evaluation.Reason;
                return new(true,false,false,incident?.IncidentId,incident?.Severity,reason,decision.CooldownRemainingSec);
            }

            var active=(await store.List<OperationalEvent>(e=>e.CameraId==command.Snapshot.CameraId && e.ZoneId==zone.ZoneId
                && e.EventTypeId==rule.EventTypeId!.Value && e.ClearedAt==null,ct)).SingleOrDefault();
            if(active is not null && AlreadyApplied(active,batch))
                return new(true,false,true,null,null,"IDEMPOTENT",0,active.EventId,active.Priority);

            if(evaluation.ShouldClear)
            {
                if(active is null) return new(true,false,false,null,null,evaluation.Reason);
                active.MetricValue=metric; active.LastObservedAt=batch.CapturedAt; active.ClearedAt=batch.CapturedAt;
                active.UpdatedAt=clock.UtcNow; active.MetadataJson=metadata; active.Status="CLEARED";
                await store.Update(active,ct);
                return new(true,false,false,null,null,evaluation.Reason,0,active.EventId,active.Priority);
            }

            if(evaluation.EligibleSeverity is null)
            {
                if(active is null) return new(true,false,false,null,null,evaluation.Reason);
                active.MetricValue=metric; active.LastObservedAt=batch.CapturedAt; active.UpdatedAt=clock.UtcNow; active.MetadataJson=metadata;
                await store.Update(active,ct);
                return new(true,false,false,null,null,evaluation.Reason,0,active.EventId,active.Priority);
            }

            if(active is null)
            {
                active=new OperationalEvent { EventId=Guid.NewGuid(),CameraId=command.Snapshot.CameraId,ZoneId=zone.ZoneId,RuleId=rule.RuleId,
                    EventTypeId=rule.EventTypeId!.Value,MetricValue=metric,
                    Priority=evaluation.EligibleSeverity,DetectedAt=batch.CapturedAt,LastObservedAt=batch.CapturedAt,UpdatedAt=clock.UtcNow,
                    CreatedAt=clock.UtcNow,MetadataJson=metadata,Status="ACTIVE" };
                await store.Add(active,ct);
            }
            else
            {
                active.MetricValue=metric; active.LastObservedAt=batch.CapturedAt;
                active.UpdatedAt=clock.UtcNow; active.MetadataJson=metadata; active.Status="ACTIVE";
                if(Rank(evaluation.EligibleSeverity)>Rank(active.Priority)) active.Priority=evaluation.EligibleSeverity;
                await store.Update(active,ct);
            }
            return new(true,false,false,null,null,evaluation.Reason,0,active.EventId,active.Priority);
        },ct);
    }

    private static string Metadata(IncidentObservationCommand command,decimal metric)=>JsonSerializer.Serialize(new {
        outcomeType=command.Rule.OutcomeType,eventTypeId=command.Rule.EventTypeId,eventTypeCode=command.Rule.EventTypeCode,measurementMode=command.Rule.Mode,thresholdUnit=command.Rule.Unit,
        command.Rule.WarningThreshold,command.Rule.CriticalThreshold,command.Rule.SustainSec,recoverySec=command.Rule.RecoverySec??command.Rule.SustainSec,
        command.Zone.ConfigId,configurationVersion=command.Zone.ConfigVersion,command.Zone.Confidence,
        videoSourceType=command.Snapshot.Connection.SourceType,command.Batch.SessionId,command.Batch.ContinuityId,command.Batch.Sequence,
        command.Batch.SourceElapsedMs,command.Batch.ConfigurationFingerprint,metric,command.Measurement.PeopleCount,command.Measurement.QueueCount },Json);

    private static bool AlreadyApplied(OperationalEvent active,AiMeasurementBatch batch)
    {
        if(string.IsNullOrWhiteSpace(active.MetadataJson)) return false;
        try
        {
            using var json=JsonDocument.Parse(active.MetadataJson);
            var root=json.RootElement;
            return root.TryGetProperty("sessionId",out var session) && session.TryGetGuid(out var sessionId) && sessionId==batch.SessionId
                && root.TryGetProperty("sequence",out var sequence) && sequence.TryGetInt64(out var value) && value>=batch.Sequence;
        }
        catch(JsonException) { return false; }
    }
    private static int Rank(string priority)=>priority switch { "CRITICAL"=>2,"WARNING"=>1,_=>0 };
}
