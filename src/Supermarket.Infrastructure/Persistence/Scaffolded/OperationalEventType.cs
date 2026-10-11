using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class OperationalEventType
{
    public Guid EventTypeId { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string MeasurementType { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public virtual ICollection<MonitoringRule> MonitoringRules { get; set; } = new List<MonitoringRule>();
    public virtual ICollection<OperationalEvent> OperationalEvents { get; set; } = new List<OperationalEvent>();
}
