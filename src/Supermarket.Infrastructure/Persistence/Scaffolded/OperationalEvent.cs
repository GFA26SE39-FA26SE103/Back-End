using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class OperationalEvent
{
    public Guid EventId { get; set; }

    public Guid CameraId { get; set; }

    public Guid ZoneId { get; set; }

    public Guid? RuleId { get; set; }

    public Guid? IncidentId { get; set; }

    public string EventType { get; set; } = null!;

    public decimal MetricValue { get; set; }

    public DateTime DetectedAt { get; set; }

    public string? MetadataJson { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual Camera Camera { get; set; } = null!;

    public virtual Incident? Incident { get; set; }

    public virtual MonitoringRule? Rule { get; set; }

    public virtual Zone Zone { get; set; } = null!;
}
