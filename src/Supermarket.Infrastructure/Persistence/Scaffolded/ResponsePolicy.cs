using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class ResponsePolicy
{
    public Guid PolicyId { get; set; }

    public Guid RuleId { get; set; }

    public string EventPriority { get; set; } = null!;

    public bool IsEnabled { get; set; }

    public int Version { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<ResponsePolicyAction> ResponsePolicyActions { get; set; } = new List<ResponsePolicyAction>();

    public virtual MonitoringRule Rule { get; set; } = null!;
}
