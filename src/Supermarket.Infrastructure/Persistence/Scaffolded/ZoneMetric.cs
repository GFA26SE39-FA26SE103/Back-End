using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class ZoneMetric
{
    public Guid MetricId { get; set; }

    public Guid ZoneId { get; set; }

    public Guid? CameraId { get; set; }

    public string MetricType { get; set; } = null!;

    public decimal MetricValue { get; set; }

    public string Unit { get; set; } = null!;

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public string? MetadataJson { get; set; }

    public DateTime CreatedAt { get; set; }

    public int? ConfigVersion { get; set; }

    public virtual Camera? Camera { get; set; }

    public virtual Zone Zone { get; set; } = null!;
}
