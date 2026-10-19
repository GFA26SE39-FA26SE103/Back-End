using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class MonitoringRule
{
    public Guid RuleId { get; set; }

    public Guid ConfigId { get; set; }

    public Guid? IncidentTypeId { get; set; }

    public decimal WarningThreshold { get; set; }

    public decimal CriticalThreshold { get; set; }

    public string ThresholdUnit { get; set; } = null!;

    public int SustainSec { get; set; }

    public int CooldownSec { get; set; }

    public string? ParametersJson { get; set; }

    public bool Enabled { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid? EventTypeId { get; set; }

    public string OutcomeType { get; set; } = null!;

    public virtual MonitoringConfiguration Config { get; set; } = null!;

    public virtual OperationalEventType? EventType { get; set; }

    public virtual IncidentType? IncidentType { get; set; }

    public virtual ICollection<Incident> Incidents { get; set; } = new List<Incident>();

    public virtual ICollection<OperationalEvent> OperationalEvents { get; set; } = new List<OperationalEvent>();

    public virtual ICollection<ResponsePolicy> ResponsePolicies { get; set; } = new List<ResponsePolicy>();
}
