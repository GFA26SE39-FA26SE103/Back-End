using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class IncidentType
{
    public Guid IncidentTypeId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public bool RequiresBeforePhoto { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<Incident> Incidents { get; set; } = new List<Incident>();

    public virtual ICollection<MonitoringRule> MonitoringRules { get; set; } = new List<MonitoringRule>();

    public virtual Slapolicy? Slapolicy { get; set; }

    public virtual ICollection<StaffReport> StaffReports { get; set; } = new List<StaffReport>();
}
