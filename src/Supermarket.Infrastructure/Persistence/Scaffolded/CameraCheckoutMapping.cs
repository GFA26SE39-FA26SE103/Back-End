using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class CameraCheckoutMapping
{
    public Guid MappingId { get; set; }

    public Guid CameraId { get; set; }

    public Guid CheckoutCounterId { get; set; }

    public string LaneRoi { get; set; } = null!;

    public string StaffRoi { get; set; } = null!;

    public bool IsEnabled { get; set; }

    public bool IsQueueSource { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? ServiceRoi { get; set; }

    public virtual Camera Camera { get; set; } = null!;

    public virtual CheckoutCounter CheckoutCounter { get; set; } = null!;
}
