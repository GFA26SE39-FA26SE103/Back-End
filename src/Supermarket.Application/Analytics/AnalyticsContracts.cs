using System.Text.Json.Serialization;

namespace Supermarket.Application;

public sealed record AnalyticsQuery(DateOnly? From = null, DateOnly? To = null, Guid? ZoneId = null);
public sealed record AnalyticsWindow(DateOnly From, DateOnly To, string TimeZone, DateTime FromUtc, DateTime ToUtcExclusive,
    DateTime GeneratedAt, Guid? ZoneId, [property: JsonIgnore] DateTime[] DayStartsUtc);
public sealed record AnalyticsDuration(int Samples, double? AverageSeconds);
public sealed record AnalyticsSla(int Evaluated, int Met, int Breached, int Pending, int Unavailable, double? MetPercent);
public sealed record AnalyticsEvents(int Total, int Active, int Cleared, int LatestCritical, AnalyticsDuration ClearTime);
public sealed record AnalyticsIncidents(int Recorded, int Total, int Open, int Closed, int Dismissed, AnalyticsDuration ResolutionTime);
public sealed record AnalyticsTasks(int Total, Dictionary<string, int> ByStatus, AnalyticsDuration ResponseTime,
    AnalyticsDuration CompletionTime, AnalyticsSla ResponseSla, AnalyticsSla ResolutionSla);
public sealed record AnalyticsSummary(AnalyticsEvents Events, AnalyticsIncidents Incidents, AnalyticsTasks Tasks);
public sealed record AnalyticsOverview(AnalyticsWindow Period, AnalyticsSummary Summary);
public sealed record AnalyticsDay(DateOnly Date, AnalyticsSummary Summary);
public sealed record AnalyticsTrends(AnalyticsWindow Period, AnalyticsDay[] Days);
public sealed record AnalyticsZone(Guid ZoneId, string ZoneName, AnalyticsSummary Summary);
public sealed record AnalyticsZones(AnalyticsWindow Period, AnalyticsZone[] Zones);

// Only grouped SQL results cross the persistence boundary, never a capped task feed or raw history.
public sealed record AnalyticsBucket(string Domain, int Day, Guid ZoneId, string Status, string? Priority, int Count,
    int ResponseSamples, double ResponseSeconds, int ResolutionSamples, double ResolutionSeconds,
    string ResponseSla, string ResolutionSla);
public interface IAnalyticsQueries
{
    Task<List<AnalyticsBucket>> Read(AnalyticsWindow window, CancellationToken ct);
}
