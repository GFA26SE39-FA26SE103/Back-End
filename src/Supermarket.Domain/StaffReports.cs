namespace Supermarket.Domain;

public sealed class StaffReport : TrackedEntity
{
    public Guid ReportId { get; set; }
    public Guid ReportedByUserId { get; set; }
    public Guid ZoneId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? OriginalInputJson { get; set; }
    public string? AiExtractedJson { get; set; }
    public Guid? IncidentTypeId { get; set; }
    public string? ReportCategory { get; set; }
    public string Status { get; set; } = "CONFIRMED";
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? ClassifiedAt { get; set; }
    public string? LocationDescription { get; set; }
}

public sealed class StaffReportMedium : Entity
{
    public Guid MediaId { get; set; }
    public Guid ReportId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string MediaType { get; set; } = "IMAGE";
    public string StorageKey { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
}
