using Microsoft.EntityFrameworkCore;
using Supermarket.Application;
using Supermarket.Infrastructure.Persistence.Scaffolded;

namespace Supermarket.Infrastructure.Persistence;

public sealed class EfHistoricalRecommendationQueries(AppDbContext db) : IHistoricalRecommendationQueries
{
    public IAsyncEnumerable<HistoricalEventSample> ClearedEvents(Guid supermarketId, AnalyticsWindow window) =>
        Events(supermarketId, window).AsAsyncEnumerable();

    internal IQueryable<HistoricalEventSample> Events(Guid supermarketId, AnalyticsWindow window) =>
        db.OperationalEvents.AsNoTracking()
            .Where(row => row.Zone.Floor.SupermarketId == supermarketId
                && row.Zone.Status == "ACTIVE"
                && (window.ZoneId == null || row.ZoneId == window.ZoneId)
                && row.DetectedAt >= window.FromUtc && row.DetectedAt < window.ToUtcExclusive
                && row.Status == "CLEARED" && row.ClearedAt >= row.DetectedAt && row.ClearedAt <= window.GeneratedAt
                && HistoricalRecommendationReader.SupportedEventTypes.Contains(row.EventType.Code))
            .Select(row => new HistoricalEventSample(row.ZoneId, row.Zone.Name, row.EventType.Code, row.EventType.Name, row.DetectedAt));
}
