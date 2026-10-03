using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class Incident
{
    public Guid IncidentId { get; set; }

    public Guid IncidentTypeId { get; set; }

    public Guid ZoneId { get; set; }

    public Guid? TriggerCameraId { get; set; }

    public Guid? ReportedByUserId { get; set; }

    public string SourceType { get; set; } = null!;

    public string Severity { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public string Status { get; set; } = null!;

    public string? StatusReason { get; set; }

    public DateTime? ClosedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual IncidentType IncidentType { get; set; } = null!;

    public virtual ICollection<OperationalEvent> OperationalEvents { get; set; } = new List<OperationalEvent>();

    public virtual UserAccount? ReportedByUser { get; set; }

    public virtual Camera? TriggerCamera { get; set; }

    public virtual Zone Zone { get; set; } = null!;
}
