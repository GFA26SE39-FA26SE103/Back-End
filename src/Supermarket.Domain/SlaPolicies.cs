namespace Supermarket.Domain;

public sealed class SlaPolicyAction : TrackedEntity
{
    public Guid ActionId { get; set; }
    public Guid PolicyId { get; set; }
    public string ActionType { get; set; } = "";
    public Guid? TargetRoleId { get; set; }
    public string ExecutionMode { get; set; } = "";
    public string? ConditionJson { get; set; }
    public string? ActionParametersJson { get; set; }
    public bool IsEnabled { get; set; }
}

public sealed class SlaPolicy : TrackedEntity
{
    public Guid PolicyId { get; set; }
    public bool IsEnabled { get; set; }
    public int Version { get; set; }
    public string ProblemDomain { get; set; } = "";
    public Guid? EventTypeId { get; set; }
    public Guid? IncidentTypeId { get; set; }
    public Guid? TaskTypeId { get; set; }
    public string PriorityCode { get; set; } = "";
    public int ResponseTargetSec { get; set; }
    public int ResolutionTargetSec { get; set; }
    public int OfferTimeoutSec { get; set; }
    public int WarningPercent { get; set; }
}
