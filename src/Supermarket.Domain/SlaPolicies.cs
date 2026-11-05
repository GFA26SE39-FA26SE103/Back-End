namespace Supermarket.Domain;

public sealed class SlaPolicy : TrackedEntity
{
    public Guid PolicyId { get; set; }
    public bool IsEnabled { get; set; }
    public int Version { get; set; }
    public string ProblemDomain { get; set; } = "";
    public Guid? EventTypeId { get; set; }
    public Guid? IncidentTypeId { get; set; }
    public string PriorityCode { get; set; } = "";
    public int ResponseTargetSec { get; set; }
    public int ResolutionTargetSec { get; set; }
    public int OfferTimeoutSec { get; set; }
    public int WarningPercent { get; set; }
}
