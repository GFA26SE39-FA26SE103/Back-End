using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class Floor
{
    public Guid FloorId { get; set; }

    public Guid SupermarketId { get; set; }

    public int FloorNumber { get; set; }

    public string Name { get; set; } = null!;

    public string? MapAssetUrl { get; set; }

    public int? MapWidth { get; set; }

    public int? MapHeight { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<Camera> Cameras { get; set; } = new List<Camera>();

    public virtual Supermarket Supermarket { get; set; } = null!;

    public virtual ICollection<Zone> Zones { get; set; } = new List<Zone>();
}
