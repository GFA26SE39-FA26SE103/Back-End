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

    public string? ColorHex { get; set; }

    public decimal? AreaM2 { get; set; }

    public int MinStaff { get; set; }

    public virtual ICollection<CameraZoneMapping> CameraZoneMappings { get; set; } = new List<CameraZoneMapping>();

    public virtual Floor Floor { get; set; } = null!;

    public virtual ICollection<Incident> Incidents { get; set; } = new List<Incident>();

    public virtual MonitoringConfiguration? MonitoringConfiguration { get; set; }

    public virtual ICollection<OperationalEvent> OperationalEvents { get; set; } = new List<OperationalEvent>();

    public virtual ICollection<Recommendation> Recommendations { get; set; } = new List<Recommendation>();

    public virtual ICollection<StaffReport> StaffReports { get; set; } = new List<StaffReport>();

    public virtual ICollection<StaffZoneAssignment> StaffZoneAssignments { get; set; } = new List<StaffZoneAssignment>();

    public virtual ICollection<Task> Tasks { get; set; } = new List<Task>();

    public virtual ICollection<ZoneAdjacency> ZoneAdjacencyAdjacentZones { get; set; } = new List<ZoneAdjacency>();

    public virtual ICollection<ZoneAdjacency> ZoneAdjacencyZones { get; set; } = new List<ZoneAdjacency>();

    public virtual ICollection<ZoneMetric> ZoneMetrics { get; set; } = new List<ZoneMetric>();
}
