using Microsoft.EntityFrameworkCore;
using Supermarket.Application;
using Supermarket.Infrastructure.Persistence.Scaffolded;

namespace Supermarket.Infrastructure.Persistence;

public sealed class EfHistoricalRecommendationQueries(AppDbContext db) : IHistoricalRecommendationQueries
{
    public IAsyncEnumerable<HistoricalRecordSample> ClearedEvents(Guid supermarketId, AnalyticsWindow window) =>
        Events(supermarketId, window).AsAsyncEnumerable();

    public IAsyncEnumerable<HistoricalRecordSample> RecordedIncidents(Guid supermarketId, AnalyticsWindow window) =>
        Incidents(supermarketId, window).AsAsyncEnumerable();

    internal IQueryable<HistoricalRecordSample> Events(Guid supermarketId, AnalyticsWindow window) =>
        db.OperationalEvents.AsNoTracking()
            .Where(row => row.Zone.Floor.SupermarketId == supermarketId
                && row.Zone.Status == "ACTIVE"
                && (window.ZoneId == null || row.ZoneId == window.ZoneId)
                && row.DetectedAt >= window.FromUtc && row.DetectedAt < window.ToUtcExclusive
                && row.Status == "CLEARED" && row.ClearedAt >= row.DetectedAt && row.ClearedAt <= window.GeneratedAt
                && HistoricalRecommendationReader.SupportedEventTypes.Contains(row.EventType.Code))
            .Select(row => new HistoricalRecordSample(row.ZoneId, row.Zone.Name, row.EventType.Code, row.EventType.Name, row.DetectedAt));

    internal IQueryable<HistoricalRecordSample> Incidents(Guid supermarketId, AnalyticsWindow window) =>
        db.Incidents.AsNoTracking()
            .Where(row => row.Zone.Floor.SupermarketId == supermarketId
                && row.Zone.Status == "ACTIVE"
                && (window.ZoneId == null || row.ZoneId == window.ZoneId)
                && row.CreatedAt >= window.FromUtc && row.CreatedAt < window.ToUtcExclusive && row.CreatedAt <= window.GeneratedAt
                && row.Status != "DISMISSED")
            .Select(row => new HistoricalRecordSample(row.ZoneId, row.Zone.Name, row.IncidentType.Code, row.IncidentType.Name, row.CreatedAt));
}
