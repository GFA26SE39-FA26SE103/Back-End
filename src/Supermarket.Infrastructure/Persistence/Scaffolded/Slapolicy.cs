using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class Slapolicy
{
    public Guid PolicyId { get; set; }

    public bool IsEnabled { get; set; }

    public int Version { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string ProblemDomain { get; set; } = null!;

    public Guid? EventTypeId { get; set; }

    public Guid? IncidentTypeId { get; set; }

    public Guid? TaskTypeId { get; set; }

    public string PriorityCode { get; set; } = null!;

    public int ResponseTargetSec { get; set; }

    public int ResolutionTargetSec { get; set; }

    public int OfferTimeoutSec { get; set; }

    public int WarningPercent { get; set; }

    public virtual OperationalEventType? EventType { get; set; }

    public virtual IncidentType? IncidentType { get; set; }

    public virtual TaskType? TaskType { get; set; }

    public virtual ICollection<Incident> Incidents { get; set; } = new List<Incident>();

    public virtual ICollection<SlapolicyAction> SlapolicyActions { get; set; } = new List<SlapolicyAction>();

    public virtual ICollection<Task> Tasks { get; set; } = new List<Task>();
}
