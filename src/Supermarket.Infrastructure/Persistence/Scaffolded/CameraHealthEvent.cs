using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class CameraHealthEvent
{
    public Guid HealthEventId { get; set; }

    public Guid CameraId { get; set; }

    public string EventType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime DetectedAt { get; set; }

    public Guid? InvestigatedByUserId { get; set; }

    public DateTime? InvestigatedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }

    public string? ResolutionNote { get; set; }

    public virtual Camera Camera { get; set; } = null!;

    public virtual UserAccount? InvestigatedByUser { get; set; }
}
