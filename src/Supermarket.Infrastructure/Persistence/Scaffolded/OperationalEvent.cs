using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class OperationalEvent
{
    public Guid EventId { get; set; }

    public Guid CameraId { get; set; }

    public Guid ZoneId { get; set; }

    public Guid? RuleId { get; set; }

    public decimal MetricValue { get; set; }

    public DateTime DetectedAt { get; set; }

    public string? MetadataJson { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public Guid EventTypeId { get; set; }

    public string Priority { get; set; } = null!;

    public DateTime LastObservedAt { get; set; }

    public DateTime? ClearedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Camera Camera { get; set; } = null!;

    public virtual OperationalEventType EventType { get; set; } = null!;

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual ICollection<Recommendation> Recommendations { get; set; } = new List<Recommendation>();

    public virtual MonitoringRule? Rule { get; set; }

    public virtual ICollection<Task> Tasks { get; set; } = new List<Task>();

    public virtual Zone Zone { get; set; } = null!;
}
