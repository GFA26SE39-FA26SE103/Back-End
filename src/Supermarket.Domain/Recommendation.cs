namespace Supermarket.Domain;

public sealed class Recommendation : Entity
{
    public Guid RecommendationId { get; set; }
    public Guid SupermarketId { get; set; }
    public Guid? ZoneId { get; set; }
    public string RecommendationType { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? SupportingDataJson { get; set; }
    public string Status { get; set; } = RecommendationStatus.Available;
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime GeneratedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public Guid? EventId { get; set; }
}
