using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class ServiceHealthEvent
{
    public Guid HealthEventId { get; set; }

    public string ServiceName { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? ErrorSummary { get; set; }

    public DateTime DetectedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }
}
