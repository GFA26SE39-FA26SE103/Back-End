using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class ShiftAssignment
{
    public Guid ShiftAssignmentId { get; set; }

    public Guid UserId { get; set; }

    public Guid ShiftId { get; set; }

    public DateOnly WorkDate { get; set; }

    public DateTime? CheckInAt { get; set; }

    public DateTime? CheckOutAt { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Shift Shift { get; set; } = null!;

    public virtual ICollection<StaffZoneAssignment> StaffZoneAssignments { get; set; } = new List<StaffZoneAssignment>();

    public virtual UserAccount User { get; set; } = null!;
}
