using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class Camera
{
    public Guid CameraId { get; set; }

    public Guid FloorId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Manufacturer { get; set; }

    public string? Model { get; set; }

    public string? SerialNumber { get; set; }

    public DateTime? InstalledAt { get; set; }

    public DateTime? WarrantyExpiresAt { get; set; }

    public decimal? MapX { get; set; }

    public decimal? MapY { get; set; }

    public decimal? MapRotationDeg { get; set; }

    public string Status { get; set; } = null!;

    public string HealthStatus { get; set; } = null!;

    public DateTime? LastSeenAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual CameraConnection? CameraConnection { get; set; }

    public virtual ICollection<CameraHealthEvent> CameraHealthEvents { get; set; } = new List<CameraHealthEvent>();

    public virtual ICollection<CameraZoneMapping> CameraZoneMappings { get; set; } = new List<CameraZoneMapping>();

    public virtual Floor Floor { get; set; } = null!;

    public virtual ICollection<Incident> Incidents { get; set; } = new List<Incident>();

    public virtual ICollection<OperationalEvent> OperationalEvents { get; set; } = new List<OperationalEvent>();
}
