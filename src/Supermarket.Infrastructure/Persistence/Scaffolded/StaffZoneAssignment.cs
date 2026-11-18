using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class StaffZoneAssignment
{
    public Guid StaffZoneAssignmentId { get; set; }

    public Guid ShiftAssignmentId { get; set; }

    public Guid ZoneId { get; set; }

    public string Status { get; set; } = null!;

    public DateTime AssignedAt { get; set; }

    public DateTime? UnassignedAt { get; set; }

    public virtual ShiftAssignment ShiftAssignment { get; set; } = null!;

    public virtual Zone Zone { get; set; } = null!;
}
