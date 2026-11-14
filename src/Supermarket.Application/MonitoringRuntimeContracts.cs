using Supermarket.Domain;
namespace Supermarket.Application;

// Private worker snapshots: never returned by a controller or logged.
public sealed record MonitoringRuleSnapshot(Guid RuleId,Guid? IncidentTypeId,string Code,string Name,string Mode,string Unit,
    decimal WarningThreshold,decimal CriticalThreshold,int SustainSec,int CooldownSec,Guid? EventTypeId=null,string EventTypeCode="",int? ClearDurationSec=null,
    string OutcomeType="OPERATIONAL_EVENT",string? ParametersJson=null);
public sealed record MonitoringZoneSnapshot(Guid ZoneId,string ZoneName,Guid ConfigId,DateTime ConfigVersion,Point[] Roi,
    decimal Confidence,MonitoringRuleSnapshot[] Rules,string? IssueCode=null);
public sealed record MonitoringCameraSnapshot(Guid CameraId,CameraConnection Connection,string Fingerprint,MonitoringZoneSnapshot[] Zones,string? IssueCode=null,
    CheckoutMappingSnapshot[]? CheckoutMappings=null);
public sealed record AiZoneMeasurement(Guid ZoneId,Guid ConfigId,DateTime ConfigVersion,int PeopleCount,int QueueCount);
public sealed record AiMeasurementBatch(int ProtocolVersion,Guid SessionId,Guid ContinuityId,long Sequence,DateTime CapturedAt,
    long SourceElapsedMs,string ConfigurationFingerprint,AiZoneMeasurement[] Zones,AiCheckoutObservation[]? Checkouts=null);
public sealed record AiMeasurementPage(int ProtocolVersion,Guid SessionId,long OldestSequence,long NewestSequence,bool Gap,
    AiMeasurementBatch[] Batches,string State,string? ErrorCode);
public sealed record IncidentObservationCommand(MonitoringCameraSnapshot Snapshot,MonitoringZoneSnapshot Zone,MonitoringRuleSnapshot Rule,
    AiMeasurementBatch Batch,AiZoneMeasurement Measurement,RuleEvaluationResult Evaluation,bool ShouldSample,
    decimal? MetricOverride=null,object? CheckoutEvidence=null,MonitoringCameraSnapshot[]? AggregateSources=null,
    DateTime? EvidenceValidUntil=null);
public sealed record IncidentWriteResult(bool Applied,bool Stale,bool Idempotent,Guid? IncidentId,string Reason,
    decimal CooldownRemainingSec=0,Guid? OperationalEventId=null,string? OperationalEventPriority=null);
public sealed record IncidentFeedQuery(int Limit=20,DateTime? AfterCreatedAt=null,Guid? AfterIncidentId=null,bool IncludeEnded=false)
{
    public void Validate()
    {
        if(Limit is <1 or >100 || (AfterCreatedAt is null)!=(AfterIncidentId is null))
            throw new ApplicationException("INVALID_FEED_QUERY","Use limit 1–100 and both cursor fields together.",400);
    }
}
public sealed record IncidentFeedItem(Guid IncidentId,Guid ZoneId,string ZoneName,string IncidentCode,string IncidentName,Guid? TriggerCameraId,
    string? TriggerCameraName,string Status,string Title,DateTime CreatedAt,DateTime UpdatedAt,DateTime? ClosedAt,
    Guid? SlaPolicyId,string? SlaPriorityCode,int? SlaPolicyVersion,DateTime? ResponseDeadlineAt,DateTime? ResolutionDeadlineAt,DateTime? MitigatedAt,
    decimal? MetricValue,string? MeasurementMode,string? ThresholdUnit,string VideoSourceType,bool IsDemo);
public sealed record IncidentFeedView(IncidentFeedItem[] Items,bool HasMore,DateTime? NextCreatedAt,Guid? NextIncidentId);
public static class MonitoringSettings
{
    public static MonitoringEvaluationSettings Settings(this MonitoringRuleSnapshot r)
    {
        var (warningExit, criticalExit) = RuleHysteresis.Read(r.ParametersJson);
        return new(r.Mode,r.Unit,r.WarningThreshold,r.CriticalThreshold,r.SustainSec,r.CooldownSec,true,r.ClearDurationSec,warningExit,criticalExit);
    }

    /// <summary>Adds the effective exit thresholds (dual threshold) of the rule to its runtime view.</summary>
    public static MonitoringRuleRuntimeView WithExits(this MonitoringRuleRuntimeView view, MonitoringRuleSnapshot rule)
    {
        var settings = rule.Settings();
        return view with { WarningExitThreshold = settings.WarningExit, CriticalExitThreshold = settings.CriticalExit };
    }
}
public sealed record MonitoringRuleRuntimeView(Guid RuleId,string IncidentCode,string IncidentName,string Mode,string Unit,decimal WarningThreshold,
    decimal CriticalThreshold,int SustainSec,int CooldownSec,int ClearDurationSec,decimal? MetricValue,long WarningProgressMs,long CriticalProgressMs,
    decimal CooldownRemainingSec,string Reason,Guid? IncidentId=null,string? OperationalEventPriority=null,Guid? OperationalEventId=null,
    decimal? WarningExitThreshold=null,decimal? CriticalExitThreshold=null,
    // Level the evaluator currently holds after sustain and dead band (null = none); the event priority itself never decreases.
    string? CurrentLevel=null);
public sealed record MonitoringZoneRuntimeView(Guid ZoneId,string ZoneName,Guid? ConfigId,DateTime? ConfigVersion,string ConfigurationStatus,
    decimal Confidence,int? PeopleCount,int? QueueCount,MonitoringRuleRuntimeView[] Rules,string? IssueCode=null);
public sealed record MonitoringCameraRuntimeView(Guid CameraId,string State,string Reason,string? ErrorCode,string? VideoSourceType,
    Guid? SessionId,long? SourceElapsedMs,DateTime? ObservedAt,bool IsStale,string? AnnotationContext,MonitoringZoneRuntimeView[] Zones,
    bool HasCheckoutMonitoring=false);
