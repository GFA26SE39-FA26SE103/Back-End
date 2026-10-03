using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Supermarket.Domain;
namespace Supermarket.Application;

public sealed class MonitoringIncidentWriter(ISetupStore store,IMonitoringSnapshotReader snapshots,IMonitoringIncidentQueries queries,IClock clock)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public static Guid EventIdentity(Guid sessionId,long sequence,Guid zoneId,Guid ruleId)
        =>new(SHA256.HashData(Encoding.UTF8.GetBytes($"FA26SE103:MF02:{sessionId:N}:{sequence}:{zoneId:N}:{ruleId:N}"))[..16]);

    public Task<IncidentWriteResult> Apply(IncidentObservationCommand command,CancellationToken ct)
    {
        var evaluation=command.Evaluation;
        if(!evaluation.Accepted) return Task.FromResult(new IncidentWriteResult(false,false,false,null,null,evaluation.Reason));
        if(!command.ShouldSample && evaluation.EligibleSeverity is null)
            return Task.FromResult(new IncidentWriteResult(true,false,false,null,null,evaluation.Reason));
        return store.Transaction(async()=>
        {
            if(!await snapshots.IsCurrent(command.Snapshot,command.Zone.ZoneId,ct))
                return new IncidentWriteResult(false,true,false,null,null,"CONFIGURATION_CHANGED");
            var rule=command.Rule; var zone=command.Zone; var batch=command.Batch;
            var eventId=EventIdentity(batch.SessionId,batch.Sequence,zone.ZoneId,rule.RuleId);
            var oldEvent=await store.Find<OperationalEvent>(eventId,ct);
            if(oldEvent is not null) return new(true,false,true,oldEvent.IncidentId,null,"IDEMPOTENT");
            var existing=(await store.List<Incident>(i=>i.ZoneId==zone.ZoneId && i.IncidentTypeId==rule.IncidentTypeId && i.ClosedAt==null,ct)).SingleOrDefault();
            var latestEnded=existing is null?await queries.LatestEndedAt(zone.ZoneId,rule.IncidentTypeId,ct):null;
            var decision=IncidentPolicy.Decide(existing,latestEnded,evaluation.EligibleSeverity,rule.Settings(),clock.UtcNow);
            var isTrigger=decision.Action=="CREATE" || decision.Escalated;
            if(!command.ShouldSample && !isTrigger)
                return new(true,false,false,existing?.IncidentId,existing?.Severity,decision.Action=="SUPPRESSED"?"COOLDOWN":evaluation.Reason,decision.CooldownRemainingSec);
            var metric=rule.Mode=="QUEUE_LENGTH"?command.Measurement.QueueCount:command.Measurement.PeopleCount;
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
                incident.Description=$"{rule.Mode}: {metric} {rule.Unit}. Latest aggregate observation; a lower count does not close this incident.";
                await store.Update(incident,ct);
            }
            var metadata=JsonSerializer.Serialize(new { measurementMode=rule.Mode,thresholdUnit=rule.Unit,rule.WarningThreshold,rule.CriticalThreshold,
                rule.SustainSec,rule.CooldownSec,zone.ConfigId,configurationVersion=zone.ConfigVersion,zone.Confidence,
                videoSourceType=command.Snapshot.Connection.SourceType,batch.SessionId,batch.ContinuityId,batch.Sequence,batch.SourceElapsedMs,
                configurationFingerprint=batch.ConfigurationFingerprint,command.Measurement.PeopleCount,command.Measurement.QueueCount },Json);
            await store.Add(new OperationalEvent { EventId=eventId,CameraId=command.Snapshot.CameraId,ZoneId=zone.ZoneId,RuleId=rule.RuleId,
                IncidentId=incident?.IncidentId,EventType=rule.Mode,MetricValue=metric,DetectedAt=batch.CapturedAt,CreatedAt=clock.UtcNow,
                MetadataJson=metadata,Status=incident is not null?"INCIDENT_LINKED":decision.Action=="SUPPRESSED"?"SUPPRESSED":"OBSERVED" },ct);
            return new(true,false,false,incident?.IncidentId,incident?.Severity,decision.Action=="SUPPRESSED"?"COOLDOWN":evaluation.Reason,decision.CooldownRemainingSec);
        },ct);
    }
}
