using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class CameraZoneMapping
{
    public Guid CameraZoneId { get; set; }

    public Guid CameraId { get; set; }

    public Guid ZoneId { get; set; }

    public string RoiPolygon { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Camera Camera { get; set; } = null!;

    public virtual Zone Zone { get; set; } = null!;
}
