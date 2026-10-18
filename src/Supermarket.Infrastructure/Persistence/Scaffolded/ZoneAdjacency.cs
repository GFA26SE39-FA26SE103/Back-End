using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class ZoneAdjacency
{
    public Guid ZoneAdjacencyId { get; set; }

    public Guid ZoneId { get; set; }

    public Guid AdjacentZoneId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Zone AdjacentZone { get; set; } = null!;

    public virtual Zone Zone { get; set; } = null!;
}
