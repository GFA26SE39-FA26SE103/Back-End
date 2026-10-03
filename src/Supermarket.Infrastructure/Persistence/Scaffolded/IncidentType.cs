using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class IncidentType
{
    public Guid IncidentTypeId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string SourceType { get; set; } = null!;

    public string? MeasurementType { get; set; }

    public bool RequiresBeforePhoto { get; set; }

    public string DefaultSeverity { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<MonitoringRule> MonitoringRules { get; set; } = new List<MonitoringRule>();
}
