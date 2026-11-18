using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class StaffReportMedium
{
    public Guid MediaId { get; set; }

    public Guid ReportId { get; set; }

    public Guid UploadedByUserId { get; set; }

    public string MediaType { get; set; } = null!;

    public string StorageKey { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public long SizeBytes { get; set; }

    public DateTime UploadedAt { get; set; }

    public virtual StaffReport Report { get; set; } = null!;

    public virtual UserAccount UploadedByUser { get; set; } = null!;
}
