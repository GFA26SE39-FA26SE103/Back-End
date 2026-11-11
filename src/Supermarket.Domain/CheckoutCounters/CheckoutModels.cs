namespace Supermarket.Domain;

// Scalar names and types must match the database-first scaffold for EfSetupStore.
public sealed class CheckoutCounter : TrackedEntity
{
    public Guid CheckoutCounterId { get; set; }
    public Guid ZoneId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsEnabled { get; set; }
}

public sealed class CameraCheckoutMapping : TrackedEntity
{
    public Guid MappingId { get; set; }
    public Guid CameraId { get; set; }
    public Guid CheckoutCounterId { get; set; }
    public string LaneRoi { get; set; } = "[]";
    public string StaffRoi { get; set; } = "[]";
    public bool IsEnabled { get; set; }
    public bool IsQueueSource { get; set; }
}
