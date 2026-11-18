using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class Incident
{
    public Guid IncidentId { get; set; }

    public Guid IncidentTypeId { get; set; }

    public Guid ZoneId { get; set; }

    public Guid? ReportedByUserId { get; set; }

    public string SourceType { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public string Status { get; set; } = null!;

    public string? StatusReason { get; set; }

    public DateTime? ClosedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid? StaffReportId { get; set; }

    public Guid? MonitoringRuleId { get; set; }

    public Guid? SlaPolicyId { get; set; }

    public string? SlaSnapshotJson { get; set; }

    public DateTime? ResolutionDeadlineAt { get; set; }

    public DateTime? MitigatedAt { get; set; }

    public virtual ICollection<IncidentMedium> IncidentMedia { get; set; } = new List<IncidentMedium>();

    public virtual IncidentType IncidentType { get; set; } = null!;

    public virtual MonitoringRule? MonitoringRule { get; set; }

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual UserAccount? ReportedByUser { get; set; }

    public virtual Slapolicy? SlaPolicy { get; set; }

    public virtual StaffReport? StaffReport { get; set; }

    public virtual ICollection<Task> Tasks { get; set; } = new List<Task>();

    public virtual Zone Zone { get; set; } = null!;
}
