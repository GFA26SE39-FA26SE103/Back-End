using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class Task
{
    public Guid TaskId { get; set; }

    public Guid? IncidentId { get; set; }

    public Guid ZoneId { get; set; }

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public string Priority { get; set; } = null!;

    public string Status { get; set; } = null!;

    public Guid? CurrentAssigneeUserId { get; set; }

    public Guid? AssignedByUserId { get; set; }

    public DateTime? AssignedAt { get; set; }

    public DateTime? AcceptedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? SubmittedAt { get; set; }

    public Guid? VerifiedByUserId { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string OriginType { get; set; } = null!;

    public Guid? EventId { get; set; }

    public Guid? StaffReportId { get; set; }

    public DateTime? PlannedStartAt { get; set; }

    public int RejectionCount { get; set; }

    public virtual UserAccount? AssignedByUser { get; set; }

    public virtual UserAccount? CurrentAssigneeUser { get; set; }

    public virtual OperationalEvent? Event { get; set; }

    public virtual Incident? Incident { get; set; }

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual StaffReport? StaffReport { get; set; }

    public virtual ICollection<TaskAssignmentHistory> TaskAssignmentHistories { get; set; } = new List<TaskAssignmentHistory>();

    public virtual ICollection<TaskEvidence> TaskEvidences { get; set; } = new List<TaskEvidence>();

    public virtual UserAccount? VerifiedByUser { get; set; }

    public virtual Zone Zone { get; set; } = null!;
}
