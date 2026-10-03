namespace Supermarket.Domain;

public sealed class Incident : TrackedEntity
{
    public Guid IncidentId { get; set; }
    public Guid IncidentTypeId { get; set; }
    public Guid ZoneId { get; set; }
    public Guid? TriggerCameraId { get; set; }
    public Guid? ReportedByUserId { get; set; }
    public string SourceType { get; set; } = "AI_DETECTED";
    public string Severity { get; set; } = "WARNING";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = "DETECTED";
    public string? StatusReason { get; set; }
    public DateTime? ClosedAt { get; set; }
}

public sealed class OperationalEvent : Entity
{
    public Guid EventId { get; set; }
    public Guid CameraId { get; set; }
    public Guid ZoneId { get; set; }
    public Guid? RuleId { get; set; }
    public Guid? IncidentId { get; set; }
    public string EventType { get; set; } = "";
    public decimal MetricValue { get; set; }
    public DateTime DetectedAt { get; set; }
    public string? MetadataJson { get; set; }
    public string Status { get; set; } = "OBSERVED";
    public DateTime CreatedAt { get; set; }
}
