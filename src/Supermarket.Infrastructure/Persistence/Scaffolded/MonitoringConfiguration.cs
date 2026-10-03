using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class MonitoringConfiguration
{
    public Guid ConfigId { get; set; }

    public Guid ZoneId { get; set; }

    public string Name { get; set; } = null!;

    public decimal ConfidenceThreshold { get; set; }

    public string Status { get; set; } = null!;

    public Guid CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual UserAccount CreatedByUser { get; set; } = null!;

    public virtual ICollection<MonitoringRule> MonitoringRules { get; set; } = new List<MonitoringRule>();

    public virtual Zone Zone { get; set; } = null!;
}
