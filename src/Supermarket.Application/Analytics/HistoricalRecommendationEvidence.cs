namespace Supermarket.Application;

public sealed record HistoricalEventSample(Guid ZoneId, string ZoneName, string EventType, string EventTypeName, DateTime DetectedAt);

public sealed record HistoricalRecommendationEvidence(DateOnly From, DateOnly To, string TimeZone, Guid? FilterZoneId,
    Guid ZoneId, string ZoneName, string EventType, string EventTypeName, int LocalHour, int EventCount, int DaysWithEvents)
{
    public int PeriodDays => To.DayNumber - From.DayNumber + 1;
    public string EvidenceVersion => "historical-pattern-v1";
}

public sealed record HistoricalRecommendationResult(RecommendationView[] Recommendations, string? Reason);

public interface IHistoricalRecommendationQueries
{
    IAsyncEnumerable<HistoricalEventSample> ClearedEvents(Guid supermarketId, AnalyticsWindow window);
}

public sealed class HistoricalRecommendationReader(IHistoricalRecommendationQueries queries, StoreCalendar calendar)
{
    public const int MinimumDistinctDays = 2;
    public const string ReviewCoverage = "REVIEW_HISTORICAL_CHECKOUT_COVERAGE";
    public const string InspectZone = "INSPECT_HISTORICAL_CONGESTION";

    public static string[] SupportedEventTypes { get; } = ["LONG_QUEUE", "EXCESSIVE_WAIT", "CHECKOUT_CAPACITY", "CONGESTION"];

    public static string ActionFor(string eventType) => eventType == "CONGESTION" ? InspectZone : ReviewCoverage;

    public async Task<HistoricalRecommendationEvidence[]> Read(Guid supermarketId, AnalyticsWindow window, CancellationToken ct)
    {
        var groups = new Dictionary<(Guid ZoneId, string EventType, int Hour), Counts>();
        await foreach (var item in queries.ClearedEvents(supermarketId, window).WithCancellation(ct))
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(item.DetectedAt, DateTimeKind.Utc), calendar.TimeZone);
            var key = (item.ZoneId, item.EventType, local.Hour);
            if (!groups.TryGetValue(key, out var counts))
                groups.Add(key, counts = new Counts(item.ZoneName, item.EventTypeName));
            counts.Days.Add(DateOnly.FromDateTime(local));
            counts.Episodes++;
        }

        return groups.Where(pair => pair.Value.Days.Count >= MinimumDistinctDays)
            .Select(pair => new HistoricalRecommendationEvidence(window.From, window.To, window.TimeZone, window.ZoneId,
                pair.Key.ZoneId, pair.Value.ZoneName, pair.Key.EventType, pair.Value.EventTypeName, pair.Key.Hour,
                pair.Value.Episodes, pair.Value.Days.Count))
            .OrderByDescending(pattern => pattern.DaysWithEvents).ThenByDescending(pattern => pattern.EventCount)
            .ThenBy(pattern => pattern.ZoneId).ThenBy(pattern => pattern.LocalHour)
            .ThenBy(pattern => pattern.EventType, StringComparer.Ordinal).ToArray();
    }

    private sealed class Counts(string zoneName, string eventTypeName)
    {
        public string ZoneName { get; } = zoneName;
        public string EventTypeName { get; } = eventTypeName;
        public int Episodes { get; set; }
        public HashSet<DateOnly> Days { get; } = [];
    }
}
