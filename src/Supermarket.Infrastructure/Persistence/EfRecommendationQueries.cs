using Microsoft.EntityFrameworkCore;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.Persistence.Scaffolded;
using Recommendation = Supermarket.Domain.Recommendation;

namespace Supermarket.Infrastructure.Persistence;

public sealed class EfRecommendationQueries(AppDbContext db) : IRecommendationQueries
{
    public async Task<RecommendationPage> List(Guid supermarketId, RecommendationQuery query, DateTime now, CancellationToken ct)
    {
        var rows = db.Recommendations.AsNoTracking().Where(row => row.SupermarketId == supermarketId);
        if (query.ZoneId is { } zoneId)
            rows = rows.Where(row => row.ZoneId == zoneId);
        if (query.Status == RecommendationStatus.Expired)
            rows = rows.Where(row => row.Status == RecommendationStatus.Expired
                || ((row.Status == RecommendationStatus.Available || row.Status == RecommendationStatus.Deferred) && row.ExpiresAt <= now));
        else if (query.Status is RecommendationStatus.Available or RecommendationStatus.Deferred)
            rows = rows.Where(row => row.Status == query.Status && (row.ExpiresAt == null || row.ExpiresAt > now));
        else if (query.Status is { } status)
            rows = rows.Where(row => row.Status == status);

        var page = await rows.OrderByDescending(row => row.GeneratedAt).ThenByDescending(row => row.RecommendationId)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize + 1)
            .Select(row => new Recommendation
            {
                RecommendationId = row.RecommendationId, ZoneId = row.ZoneId, EventId = row.EventId,
                RecommendationType = row.RecommendationType, Title = row.Title, Description = row.Description,
                Status = row.Status, GeneratedAt = row.GeneratedAt, ExpiresAt = row.ExpiresAt,
                ReviewedByUserId = row.ReviewedByUserId, ReviewedAt = row.ReviewedAt
            }).ToArrayAsync(ct);

        return new(page.Take(query.PageSize).Select(row => RecommendationWork.View(row, now)).ToArray(),
            query.Page, query.PageSize, page.Length > query.PageSize);
    }
}
