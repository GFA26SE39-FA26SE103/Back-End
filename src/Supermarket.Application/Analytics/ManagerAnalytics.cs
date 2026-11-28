using Supermarket.Domain;

namespace Supermarket.Application;

public sealed class ManagerAnalytics(IAnalyticsQueries queries, ISetupStore store, ICurrentUser user, IClock clock,
    StoreCalendar calendar, IReadCache? cache = null)
{
    private static readonly TimeSpan DashboardCacheLifetime = TimeSpan.FromSeconds(30);
    private sealed record DashboardSnapshot(DateTime GeneratedAt, List<AnalyticsBucket> Rows);

    private Task<AnalyticsWindow> Window(AnalyticsQuery query, CancellationToken ct)
    {
        UseCase.Manager(user);
        return AnalyticsWindowResolver.Resolve(query, store, clock, calendar, ct);
    }

    public async Task<AnalyticsOverview> Overview(AnalyticsQuery query, CancellationToken ct)
    {
        var (window, rows) = await Dashboard(query, ct);
        return new(window, Summarize(rows));
    }

    public async Task<AnalyticsTrends> Trends(AnalyticsQuery query, CancellationToken ct)
    {
        var (window, data) = await Dashboard(query, ct);
        var rows = data.ToLookup(row => row.Day);
        return new(window, Enumerable.Range(0, window.DayStartsUtc.Length)
            .Select(day => new AnalyticsDay(window.From.AddDays(day), Summarize(rows[day]))).ToArray());
    }

    public async Task<AnalyticsZones> Zones(AnalyticsQuery query, CancellationToken ct)
    {
        var (window, data) = await Dashboard(query, ct);
        var zones = await store.List<Zone>(z => window.ZoneId == null || z.ZoneId == window.ZoneId, ct);
        var rows = data.ToLookup(row => row.ZoneId);
        return new(window, zones.OrderBy(zone => zone.Name).ThenBy(zone => zone.ZoneId)
            .Select(zone => new AnalyticsZone(zone.ZoneId, zone.Name, Summarize(rows[zone.ZoneId]))).ToArray());
    }

    private async Task<(AnalyticsWindow Window, List<AnalyticsBucket> Rows)> Dashboard(AnalyticsQuery query, CancellationToken ct)
    {
        var window = await Window(query, ct);
        if (cache is null) return (window, await queries.Read(window, ct));

        var key = FormattableString.Invariant($"manager-analytics:v1:{window.From:yyyy-MM-dd}:{window.To:yyyy-MM-dd}:{window.TimeZone}:{window.ZoneId:N}");
        var snapshot = await cache.GetOrCreate(key, DashboardCacheLifetime,
            async token => new DashboardSnapshot(window.GeneratedAt, await queries.Read(window, token)), ct);
        return (window with { GeneratedAt = snapshot.GeneratedAt }, snapshot.Rows);
    }

    public async Task<AnalyticsReportData> Report(AnalyticsQuery query, CancellationToken ct)
    {
        var window = await Window(query, ct);
        var data = await queries.Read(window, ct);
        var byDay = data.ToLookup(row => row.Day);
        var byZone = data.ToLookup(row => row.ZoneId);
        var zones = await store.List<Zone>(z => window.ZoneId == null || z.ZoneId == window.ZoneId, ct);
        return new(window, Summarize(data),
            Enumerable.Range(0, window.DayStartsUtc.Length)
                .Select(day => new AnalyticsDay(window.From.AddDays(day), Summarize(byDay[day]))).ToArray(),
            zones.OrderBy(zone => zone.Name).ThenBy(zone => zone.ZoneId)
                .Select(zone => new AnalyticsZone(zone.ZoneId, zone.Name, Summarize(byZone[zone.ZoneId]))).ToArray(),
            [
                "Incident KPI totals exclude records whose current status is DISMISSED; recorded and dismissed counts are retained for reconciliation.",
                "Missing averages and SLA rates are unavailable, not zero.",
                "Statuses are current at generation time; this report does not reconstruct historical end-of-day status.",
                "This export contains recorded Operational Events, Incidents and Tasks. Camera-health and AI-measurement series are unavailable in the current analytics dataset."
            ]);
    }

    private static AnalyticsSummary Summarize(IEnumerable<AnalyticsBucket> source)
    {
        var rows = source.ToArray();
        var events = rows.Where(row => row.Domain == "EVENT").ToArray();
        var incidents = rows.Where(row => row.Domain == "INCIDENT").ToArray();
        var tasks = rows.Where(row => row.Domain == "TASK").ToArray();
        static int Count(IEnumerable<AnalyticsBucket> values) => values.Sum(row => row.Count);
        static AnalyticsDuration Duration(IEnumerable<AnalyticsBucket> values, bool response = false)
        {
            var samples = values.Sum(row => response ? row.ResponseSamples : row.ResolutionSamples);
            var seconds = values.Sum(row => response ? row.ResponseSeconds : row.ResolutionSeconds);
            return new(samples, samples == 0 ? null : seconds / samples);
        }
        static AnalyticsSla Sla(AnalyticsBucket[] values, bool response)
        {
            int State(string state) => values.Where(row => (response ? row.ResponseSla : row.ResolutionSla) == state).Sum(row => row.Count);
            var met = State("MET"); var breached = State("BREACHED");
            return new(met + breached, met, breached, State("PENDING"), State("UNAVAILABLE"),
                met + breached == 0 ? null : 100.0 * met / (met + breached));
        }
        var byStatus = new[] { "OFFERED", "ACCEPTED", "IN_PROGRESS", "SUBMITTED", "VERIFIED", "CANCELLED" }.ToDictionary(status => status, _ => 0);
        foreach (var group in tasks.GroupBy(row => row.Status)) byStatus[group.Key] = Count(group);
        return new(
            new(Count(events), Count(events.Where(row => row.Status == "ACTIVE")), Count(events.Where(row => row.Status == "CLEARED")),
                Count(events.Where(row => row.Priority == "CRITICAL")), Duration(events)),
            new(Count(incidents), Count(incidents.Where(row => row.Status != "DISMISSED")), Count(incidents.Where(row => row.Status is not ("CLOSED" or "DISMISSED"))),
                Count(incidents.Where(row => row.Status == "CLOSED")), Count(incidents.Where(row => row.Status == "DISMISSED")), Duration(incidents)),
            new(Count(tasks), byStatus, Duration(tasks, true), Duration(tasks), Sla(tasks, true), Sla(tasks, false)));
    }
}
