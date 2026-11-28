namespace Supermarket.Application;

public sealed record HistoricalRecordSample(Guid ZoneId, string ZoneName, string TypeCode, string TypeName, DateTime RecordedAt);

public sealed record HistoricalRecommendationQuery(DateOnly? From = null, DateOnly? To = null, Guid? ZoneId = null, string? Source = null);

public sealed record HistoricalRecommendationEvidence(DateOnly From, DateOnly To, string TimeZone, Guid? FilterZoneId,
    Guid ZoneId, string ZoneName, string EventType, string EventTypeName, int? LocalHour, int EventCount, int DaysWithEvents,
    string Source = HistoricalRecommendationReader.Events)
{
    public int PeriodDays => To.DayNumber - From.DayNumber + 1;
    public string EvidenceVersion => "historical-pattern-v1";
}

public sealed record HistoricalRecommendationResult(RecommendationView[] Recommendations, string? Reason);

public interface IHistoricalRecommendationQueries
{
    IAsyncEnumerable<HistoricalRecordSample> ClearedEvents(Guid supermarketId, AnalyticsWindow window);
    IAsyncEnumerable<HistoricalRecordSample> RecordedIncidents(Guid supermarketId, AnalyticsWindow window);
}

public sealed class HistoricalRecommendationReader(IHistoricalRecommendationQueries queries, StoreCalendar calendar)
{
    public const int MinimumDistinctDays = 2;
    public const string Events = "EVENTS";
    public const string Incidents = "INCIDENTS";
    public const string ReviewCoverage = "REVIEW_HISTORICAL_CHECKOUT_COVERAGE";
    public const string InspectZone = "INSPECT_HISTORICAL_CONGESTION";
    public const string ReviewIncidentPrevention = "REVIEW_HISTORICAL_INCIDENT_PREVENTION";

    public static string[] SupportedEventTypes { get; } = ["LONG_QUEUE", "EXCESSIVE_WAIT", "CHECKOUT_CAPACITY", "CONGESTION"];

    public static string ActionFor(HistoricalRecommendationEvidence pattern) => pattern.Source switch
    {
        Incidents => ReviewIncidentPrevention,
        _ when pattern.EventType == "CONGESTION" => InspectZone,
        _ => ReviewCoverage
    };

    public async Task<HistoricalRecommendationEvidence[]> Read(Guid supermarketId, AnalyticsWindow window, string source, CancellationToken ct)
    {
        var byHour = source == Events;
        var records = byHour ? queries.ClearedEvents(supermarketId, window) : queries.RecordedIncidents(supermarketId, window);
        var groups = new Dictionary<(Guid ZoneId, string TypeCode, int? Hour), Counts>();
        await foreach (var item in records.WithCancellation(ct))
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(item.RecordedAt, DateTimeKind.Utc), calendar.TimeZone);
            var key = (item.ZoneId, item.TypeCode, byHour ? local.Hour : (int?)null);
            if (!groups.TryGetValue(key, out var counts))
                groups.Add(key, counts = new Counts(item.ZoneName, item.TypeName));
            counts.Days.Add(DateOnly.FromDateTime(local));
            counts.Records++;
        }

        return groups.Where(pair => pair.Value.Days.Count >= MinimumDistinctDays)
            .Select(pair => new HistoricalRecommendationEvidence(window.From, window.To, window.TimeZone, window.ZoneId,
                pair.Key.ZoneId, pair.Value.ZoneName, pair.Key.TypeCode, pair.Value.TypeName, pair.Key.Hour,
                pair.Value.Records, pair.Value.Days.Count, source))
            .OrderByDescending(pattern => pattern.DaysWithEvents).ThenByDescending(pattern => pattern.EventCount)
            .ThenBy(pattern => pattern.ZoneId).ThenBy(pattern => pattern.LocalHour)
            .ThenBy(pattern => pattern.EventType, StringComparer.Ordinal).ToArray();
    }

    private sealed class Counts(string zoneName, string typeName)
    {
        public string ZoneName { get; } = zoneName;
        public string TypeName { get; } = typeName;
        public int Records { get; set; }
        public HashSet<DateOnly> Days { get; } = [];
    }
}
