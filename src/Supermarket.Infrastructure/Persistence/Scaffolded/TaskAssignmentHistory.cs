using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class TaskAssignmentHistory
{
    public Guid TaskAssignmentId { get; set; }

    public Guid TaskId { get; set; }

    public Guid StaffUserId { get; set; }

    public Guid? AssignedByUserId { get; set; }

    public string AssignmentType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime AssignedAt { get; set; }

    public DateTime? RespondedAt { get; set; }

    public DateTime? EndedAt { get; set; }

    public string? Reason { get; set; }

    public virtual UserAccount? AssignedByUser { get; set; }

    public virtual UserAccount StaffUser { get; set; } = null!;

    public virtual Task Task { get; set; } = null!;
}
