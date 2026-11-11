using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class CheckoutCounter
{
    public Guid CheckoutCounterId { get; set; }

    public Guid ZoneId { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public bool IsEnabled { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual CameraCheckoutMapping? CameraCheckoutMapping { get; set; }

    public virtual Zone Zone { get; set; } = null!;
}
