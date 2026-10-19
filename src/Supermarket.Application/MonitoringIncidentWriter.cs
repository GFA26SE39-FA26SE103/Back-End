using System.Text.Json;
using Supermarket.Domain;
namespace Supermarket.Application;

public sealed class MonitoringIncidentWriter(ISetupStore store,IMonitoringSnapshotReader snapshots,IMonitoringIncidentQueries queries,IClock clock,IOperationalEventResponder? responder=null)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);

    public Task<IncidentWriteResult> Apply(IncidentObservationCommand command,CancellationToken ct)
    {
        var evaluation=command.Evaluation;
        if(!evaluation.Accepted) return Task.FromResult(new IncidentWriteResult(false,false,false,null,null,evaluation.Reason));
        if(!command.ShouldSample && evaluation.EligibleSeverity is null && !evaluation.ShouldClear)
            return Task.FromResult(new IncidentWriteResult(true,false,false,null,null,evaluation.Reason));
        if(command.Rule.EventTypeId==Guid.Empty || string.IsNullOrWhiteSpace(command.Rule.EventTypeCode))
            throw new ApplicationException("OPERATIONAL_EVENT_TYPE_UNAVAILABLE","The monitoring rule has no operational event type.",503);

        return store.Transaction(async()=>
        {
            if(!await snapshots.IsCurrent(command.Snapshot,command.Zone.ZoneId,ct))
                return new IncidentWriteResult(false,true,false,null,null,"CONFIGURATION_CHANGED");

            var rule=command.Rule; var zone=command.Zone; var batch=command.Batch;
            var active=(await store.List<OperationalEvent>(e=>e.CameraId==command.Snapshot.CameraId && e.ZoneId==zone.ZoneId
                && e.EventTypeId==rule.EventTypeId && e.ClearedAt==null,ct)).SingleOrDefault();
            if(active is not null && AlreadyApplied(active,batch))
                return new(true,false,true,active.IncidentId,null,"IDEMPOTENT",0,active.EventId,active.Priority);

            var metric=rule.Mode=="QUEUE_LENGTH"?command.Measurement.QueueCount:command.Measurement.PeopleCount;
            var metadata=Metadata(command,metric);
            if(evaluation.ShouldClear)
            {
                if(active is null) return new(true,false,false,null,null,evaluation.Reason);
                active.MetricValue=metric; active.LastObservedAt=batch.CapturedAt; active.ClearedAt=batch.CapturedAt;
                active.UpdatedAt=clock.UtcNow; active.MetadataJson=metadata; active.Status="CLEARED";
                await store.Update(active,ct);
                if(responder is not null) await responder.Cleared(active,ct);
                var linked=active.IncidentId is { } incidentId?await store.Find<Incident>(incidentId,ct):null;
                return new(true,false,false,active.IncidentId,linked?.Severity,evaluation.Reason,0,active.EventId,active.Priority);
            }

            if(evaluation.EligibleSeverity is null)
            {
                if(active is null) return new(true,false,false,null,null,evaluation.Reason);
                active.MetricValue=metric; active.LastObservedAt=batch.CapturedAt; active.UpdatedAt=clock.UtcNow; active.MetadataJson=metadata;
                await store.Update(active,ct);
                var linked=active.IncidentId is { } incidentId?await store.Find<Incident>(incidentId,ct):null;
                return new(true,false,false,active.IncidentId,linked?.Severity,evaluation.Reason,0,active.EventId,active.Priority);
            }

            var existing=(await store.List<Incident>(i=>i.ZoneId==zone.ZoneId && i.IncidentTypeId==rule.IncidentTypeId && i.ClosedAt==null,ct)).SingleOrDefault();
            var latestEnded=existing is null?await queries.LatestEndedAt(zone.ZoneId,rule.IncidentTypeId,ct):null;
            var decision=IncidentPolicy.Decide(existing,latestEnded,evaluation.EligibleSeverity,rule.Settings(),clock.UtcNow);
            var incident=existing;
            if(decision.Action=="CREATE")
            {
                var title=$"{rule.Name} — {zone.ZoneName}";
                incident=new Incident { IncidentId=Guid.NewGuid(),IncidentTypeId=rule.IncidentTypeId,ZoneId=zone.ZoneId,
                    TriggerCameraId=command.Snapshot.CameraId,Title=title[..Math.Min(200,title.Length)],Severity=decision.Severity!,
                    Description=$"{rule.Mode}: {metric} {rule.Unit}. AI detected; dispatch is not implemented." };
                await store.Add(incident,ct);
            }
            else if(incident is not null)
            {
                incident.Severity=decision.Severity!;
                incident.Description=$"{rule.Mode}: {metric} {rule.Unit}. Latest aggregate observation; operational-event recovery does not close this incident.";
                await store.Update(incident,ct);
            }

            if(active is null)
            {
                active=new OperationalEvent { EventId=Guid.NewGuid(),CameraId=command.Snapshot.CameraId,ZoneId=zone.ZoneId,RuleId=rule.RuleId,
                    IncidentId=incident?.IncidentId,EventTypeId=rule.EventTypeId,EventType=rule.EventTypeCode,MetricValue=metric,
                    Priority=evaluation.EligibleSeverity,DetectedAt=batch.CapturedAt,LastObservedAt=batch.CapturedAt,UpdatedAt=clock.UtcNow,
                    CreatedAt=clock.UtcNow,MetadataJson=metadata,Status="ACTIVE" };
                await store.Add(active,ct);
                if(responder is not null) await responder.Raised(active,null,ct);
            }
            else
            {
                active.IncidentId??=incident?.IncidentId; active.MetricValue=metric; active.LastObservedAt=batch.CapturedAt;
                active.UpdatedAt=clock.UtcNow; active.MetadataJson=metadata; active.Status="ACTIVE";
                var previous=active.Priority;
                if(Rank(evaluation.EligibleSeverity)>Rank(active.Priority)) active.Priority=evaluation.EligibleSeverity;
                await store.Update(active,ct);
                if(responder is not null && active.Priority!=previous) await responder.Raised(active,previous,ct);
            }
            var reason=decision.Action=="SUPPRESSED"?"COOLDOWN":evaluation.Reason;
            return new(true,false,false,incident?.IncidentId,incident?.Severity,reason,decision.CooldownRemainingSec,active.EventId,active.Priority);
        },ct);
    }

    private static string Metadata(IncidentObservationCommand command,decimal metric)=>JsonSerializer.Serialize(new {
        eventTypeId=command.Rule.EventTypeId,eventTypeCode=command.Rule.EventTypeCode,measurementMode=command.Rule.Mode,thresholdUnit=command.Rule.Unit,
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
