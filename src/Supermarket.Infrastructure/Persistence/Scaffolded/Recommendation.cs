using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class Recommendation
{
    public Guid RecommendationId { get; set; }

    public Guid SupermarketId { get; set; }

    public Guid? ZoneId { get; set; }

    public string RecommendationType { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string? SupportingDataJson { get; set; }

    public string Status { get; set; } = null!;

    public Guid? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public DateTime GeneratedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public Guid? EventId { get; set; }

    public virtual OperationalEvent? Event { get; set; }

    public virtual UserAccount? ReviewedByUser { get; set; }

    public virtual Supermarket Supermarket { get; set; } = null!;

    public virtual Zone? Zone { get; set; }
}
