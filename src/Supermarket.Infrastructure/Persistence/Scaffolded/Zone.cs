using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class Zone
{
    public Guid ZoneId { get; set; }

    public Guid FloorId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? ZoneType { get; set; }

    public string MapPolygon { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public decimal? AreaM2 { get; set; }

    public string? ColorHex { get; set; }

    public virtual ICollection<CameraZoneMapping> CameraZoneMappings { get; set; } = new List<CameraZoneMapping>();

    public virtual Floor Floor { get; set; } = null!;

    public virtual ICollection<Incident> Incidents { get; set; } = new List<Incident>();

    public virtual MonitoringConfiguration? MonitoringConfiguration { get; set; }

    public virtual ICollection<OperationalEvent> OperationalEvents { get; set; } = new List<OperationalEvent>();
}
