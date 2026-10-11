using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class ResponsePolicyAction
{
    public Guid ActionId { get; set; }

    public Guid PolicyId { get; set; }

    public string ActionType { get; set; } = null!;

    public Guid? TargetRoleId { get; set; }

    public string ExecutionMode { get; set; } = null!;

    public string? ConditionJson { get; set; }

    public string? ActionParametersJson { get; set; }

    public Guid? TargetIncidentTypeId { get; set; }

    public string? ProposedIncidentSeverity { get; set; }

    public bool IsEnabled { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ResponsePolicy Policy { get; set; } = null!;

    public virtual IncidentType? TargetIncidentType { get; set; }

    public virtual Role? TargetRole { get; set; }
}
