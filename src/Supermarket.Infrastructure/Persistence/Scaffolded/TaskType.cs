using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class TaskType
{
    public Guid TaskTypeId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public Guid RequiredSpecialtyId { get; set; }

    public string SourceCategory { get; set; } = null!;

    public virtual StaffSpecialty RequiredSpecialty { get; set; } = null!;

    public virtual Slapolicy? Slapolicy { get; set; }

    public virtual ICollection<Task> Tasks { get; set; } = new List<Task>();
}
