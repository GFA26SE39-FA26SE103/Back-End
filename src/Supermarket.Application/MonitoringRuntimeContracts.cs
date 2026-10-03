using Supermarket.Domain;
namespace Supermarket.Application;

// Private worker snapshots: never returned by a controller or logged.
public sealed record MonitoringRuleSnapshot(Guid RuleId,Guid IncidentTypeId,string Code,string Name,string Mode,string Unit,
    decimal WarningThreshold,decimal CriticalThreshold,int SustainSec,int CooldownSec);
public sealed record MonitoringZoneSnapshot(Guid ZoneId,string ZoneName,Guid ConfigId,DateTime ConfigVersion,Point[] Roi,
    decimal Confidence,MonitoringRuleSnapshot[] Rules,string? IssueCode=null);
public sealed record MonitoringCameraSnapshot(Guid CameraId,CameraConnection Connection,string Fingerprint,MonitoringZoneSnapshot[] Zones,string? IssueCode=null);
public sealed record AiZoneMeasurement(Guid ZoneId,Guid ConfigId,DateTime ConfigVersion,int PeopleCount,int QueueCount);
public sealed record AiMeasurementBatch(int ProtocolVersion,Guid SessionId,Guid ContinuityId,long Sequence,DateTime CapturedAt,
    long SourceElapsedMs,string ConfigurationFingerprint,AiZoneMeasurement[] Zones);
public sealed record AiMeasurementPage(int ProtocolVersion,Guid SessionId,long OldestSequence,long NewestSequence,bool Gap,
    AiMeasurementBatch[] Batches,string State,string? ErrorCode);
public sealed record IncidentObservationCommand(MonitoringCameraSnapshot Snapshot,MonitoringZoneSnapshot Zone,MonitoringRuleSnapshot Rule,
    AiMeasurementBatch Batch,AiZoneMeasurement Measurement,RuleEvaluationResult Evaluation,bool ShouldSample);
public sealed record IncidentWriteResult(bool Applied,bool Stale,bool Idempotent,Guid? IncidentId,string? Severity,string Reason,decimal CooldownRemainingSec=0);
public sealed record IncidentFeedQuery(int Limit=20,DateTime? AfterCreatedAt=null,Guid? AfterIncidentId=null,bool IncludeEnded=false)
{
    public void Validate()
    {
        if(Limit is <1 or >100 || (AfterCreatedAt is null)!=(AfterIncidentId is null))
            throw new ApplicationException("INVALID_FEED_QUERY","Use limit 1–100 and both cursor fields together.",400);
    }
}
public sealed record IncidentFeedItem(Guid IncidentId,Guid ZoneId,string ZoneName,string IncidentCode,string IncidentName,Guid? TriggerCameraId,
    string? TriggerCameraName,string Severity,string Status,string Title,DateTime CreatedAt,DateTime UpdatedAt,DateTime? ClosedAt,
    decimal? MetricValue,string? MeasurementMode,string? ThresholdUnit,string VideoSourceType,bool IsDemo);
public sealed record IncidentFeedView(IncidentFeedItem[] Items,bool HasMore,DateTime? NextCreatedAt,Guid? NextIncidentId);
public static class MonitoringSettings
{
    public static MonitoringEvaluationSettings Settings(this MonitoringRuleSnapshot r)=>new(r.Mode,r.Unit,r.WarningThreshold,r.CriticalThreshold,r.SustainSec,r.CooldownSec,true);
}
public sealed record MonitoringRuleRuntimeView(Guid RuleId,string IncidentCode,string IncidentName,string Mode,string Unit,decimal WarningThreshold,
    decimal CriticalThreshold,int SustainSec,int CooldownSec,decimal? MetricValue,long WarningProgressMs,long CriticalProgressMs,
    decimal CooldownRemainingSec,string Reason,Guid? IncidentId=null,string? IncidentSeverity=null);
public sealed record MonitoringZoneRuntimeView(Guid ZoneId,string ZoneName,Guid? ConfigId,DateTime? ConfigVersion,string ConfigurationStatus,
    decimal Confidence,int? PeopleCount,int? QueueCount,MonitoringRuleRuntimeView[] Rules,string? IssueCode=null);
public sealed record MonitoringCameraRuntimeView(Guid CameraId,string State,string Reason,string? ErrorCode,string? VideoSourceType,
    Guid? SessionId,long? SourceElapsedMs,DateTime? ObservedAt,bool IsStale,string? AnnotationContext,MonitoringZoneRuntimeView[] Zones);
