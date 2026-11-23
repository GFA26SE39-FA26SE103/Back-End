using System.Text.Json;
using Supermarket.Domain;
namespace Supermarket.Application;

public sealed class MonitoringIncidentWriter(ISetupStore store,IMonitoringSnapshotReader snapshots,IClock clock,
    IOperationalEventResponder? responder=null)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);

    public Task<IncidentWriteResult> Apply(IncidentObservationCommand command,CancellationToken ct)
    {
        var evaluation=command.Evaluation;
        if(!evaluation.Accepted) return Task.FromResult(new IncidentWriteResult(false,false,false,null,evaluation.Reason));
        if(!command.ShouldSample && evaluation.EligibleSeverity is null && !evaluation.ShouldClear)
            return Task.FromResult(new IncidentWriteResult(true,false,false,null,evaluation.Reason));
        var outcome=command.Rule.OutcomeType;
        if(outcome != "OPERATIONAL_EVENT")
            throw new ApplicationException("MONITORING_OUTCOME_INVALID","The monitoring rule outcome type is invalid.",503);
        if(outcome=="OPERATIONAL_EVENT" && (command.Rule.EventTypeId is not { } eventTypeId || eventTypeId==Guid.Empty || string.IsNullOrWhiteSpace(command.Rule.EventTypeCode)))
            throw new ApplicationException("OPERATIONAL_EVENT_TYPE_UNAVAILABLE","The monitoring rule has no operational event type.",503);

        return store.Transaction(async()=>
        {
            if(!(command.AggregateSources is { } sources
                ? await snapshots.IsCurrentAggregate(sources,command.Zone.ZoneId,ct)
                : await snapshots.IsCurrent(command.Snapshot,command.Zone.ZoneId,ct)))
                return new IncidentWriteResult(false,true,false,null,"CONFIGURATION_CHANGED");

            EnsureEvidenceFresh(command);

            var rule=command.Rule; var zone=command.Zone; var batch=command.Batch;
            if(MonitoringPolicy.IsCheckoutMode(rule.Mode) && command.MetricOverride is null
                || !MonitoringPolicy.IsCheckoutMode(rule.Mode) && command.MetricOverride is not null)
                throw new ApplicationException("CHECKOUT_MEASUREMENT_INVALID","Checkout rules require a validated aggregate metric.",503);
            var metric=command.MetricOverride??rule.CameraMetric(zone,command.Measurement);
            var metadata=Metadata(command,metric);

            var active=(await store.List<OperationalEvent>(e=>e.RuleId==rule.RuleId && e.ZoneId==zone.ZoneId && e.ClearedAt==null,ct)).SingleOrDefault();
            EnsureEvidenceFresh(command);
            if(active is not null && AlreadyApplied(active,batch))
                return new(true,false,true,null,"IDEMPOTENT",0,active.EventId,active.Priority);

            if(evaluation.ShouldClear)
            {
                if(active is null) return new(true,false,false,null,evaluation.Reason);
                active.MetricValue=metric; active.LastObservedAt=batch.CapturedAt; active.ClearedAt=batch.CapturedAt;
                active.UpdatedAt=clock.UtcNow; active.MetadataJson=metadata; active.Status="CLEARED";
                await store.Update(active,ct);
                if(responder is not null) await responder.Cleared(active,ct);
                return new(true,false,false,null,evaluation.Reason,0,active.EventId,active.Priority);
            }

            if(evaluation.EligibleSeverity is null)
            {
                if(active is null) return new(true,false,false,null,evaluation.Reason);
                active.MetricValue=metric; active.LastObservedAt=batch.CapturedAt; active.UpdatedAt=clock.UtcNow; active.MetadataJson=metadata;
                await store.Update(active,ct);
                return new(true,false,false,null,evaluation.Reason,0,active.EventId,active.Priority);
            }

            if(active is null)
            {
                active=new OperationalEvent { EventId=Guid.NewGuid(),CameraId=command.Snapshot.CameraId,ZoneId=zone.ZoneId,RuleId=rule.RuleId,
                    EventTypeId=rule.EventTypeId!.Value,MetricValue=metric,
                    Priority=evaluation.EligibleSeverity,DetectedAt=batch.CapturedAt,LastObservedAt=batch.CapturedAt,UpdatedAt=clock.UtcNow,
                    CreatedAt=clock.UtcNow,MetadataJson=metadata,Status="ACTIVE" };
                await store.Add(active,ct);
                if(responder is not null) await responder.Raised(active,null,ct);
            }
            else
            {
                active.MetricValue=metric; active.LastObservedAt=batch.CapturedAt;
                active.UpdatedAt=clock.UtcNow; active.MetadataJson=metadata; active.Status="ACTIVE";
                var previousPriority=active.Priority;
                if(Rank(evaluation.EligibleSeverity)>Rank(active.Priority)) active.Priority=evaluation.EligibleSeverity;
                await store.Update(active,ct);
                if(responder is not null && active.Priority!=previousPriority) await responder.Raised(active,previousPriority,ct);
            }
            return new(true,false,false,null,evaluation.Reason,0,active.EventId,active.Priority);
        },ct);
    }

    private void EnsureEvidenceFresh(IncidentObservationCommand command)
    {
        if(MonitoringPolicy.IsCheckoutMode(command.Rule.Mode)
            && (command.EvidenceValidUntil is not { } until || clock.UtcNow>until))
            throw new ApplicationException("CHECKOUT_OBSERVATION_STALE","Checkout evidence expired before persistence.",503);
    }

    private static string Metadata(IncidentObservationCommand command,decimal metric)
    {
        var metadata=JsonSerializer.SerializeToNode(new {
        outcomeType=command.Rule.OutcomeType,eventTypeId=command.Rule.EventTypeId,eventTypeCode=command.Rule.EventTypeCode,measurementMode=command.Rule.Mode,thresholdUnit=command.Rule.Unit,
        command.Rule.WarningThreshold,command.Rule.CriticalThreshold,command.Rule.SustainSec,clearDurationSec=command.Rule.ClearDurationSec??command.Rule.SustainSec,
        command.Zone.ConfigId,configurationVersion=command.Zone.ConfigVersion,command.Zone.Confidence,
        videoSourceType=command.Snapshot.Connection.SourceType,command.Batch.SessionId,command.Batch.ContinuityId,command.Batch.Sequence,
        command.Batch.SourceElapsedMs,command.Batch.ConfigurationFingerprint,metric,command.Measurement.PeopleCount,command.Measurement.QueueCount },Json)!.AsObject();
        if (command.Rule.Mode == MonitoringPolicy.DensityMode)
            metadata["areaM2"] = JsonSerializer.SerializeToNode(command.Zone.AreaM2, Json);
        if(command.CheckoutEvidence is not null) metadata["checkout"]=JsonSerializer.SerializeToNode(command.CheckoutEvidence,Json);
        return metadata.ToJsonString(Json);
    }

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
