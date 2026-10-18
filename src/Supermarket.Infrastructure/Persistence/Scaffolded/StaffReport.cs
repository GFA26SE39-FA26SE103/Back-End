using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class StaffReport
{
    public Guid ReportId { get; set; }

    public Guid ReportedByUserId { get; set; }

    public Guid ZoneId { get; set; }

    public string ReportCategory { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public string? OriginalInputJson { get; set; }

    public string? AiExtractedJson { get; set; }

    public string? Classification { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? ConfirmedAt { get; set; }

    public DateTime? ClassifiedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Incident? Incident { get; set; }

    public virtual UserAccount ReportedByUser { get; set; } = null!;

    public virtual ICollection<StaffReportMedium> StaffReportMedia { get; set; } = new List<StaffReportMedium>();

    public virtual ICollection<Task> Tasks { get; set; } = new List<Task>();

    public virtual Zone Zone { get; set; } = null!;
}
