using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class StaffUnavailability
{
    public Guid UnavailabilityId { get; set; }

    public Guid UserId { get; set; }

    public DateTime StartAt { get; set; }

    public DateTime EndAt { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual UserAccount User { get; set; } = null!;
}
