using Supermarket.Domain;

namespace Supermarket.Application;

public sealed class ManagerAnalytics(IAnalyticsQueries queries, ISetupStore store, ICurrentUser user, IClock clock, StoreCalendar calendar)
{
    private async Task<AnalyticsWindow> Window(AnalyticsQuery query, CancellationToken ct)
    {
        UseCase.Manager(user);
        var now = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        var today = calendar.Today(now);
        var to = query.To ?? today;
        if (to.Year < 2000 || to > today)
            throw new ApplicationException("INVALID_ANALYTICS_QUERY", "The end date must be from year 2000 through today.", 400);
        var from = query.From ?? to.AddDays(-6);
        if (from > to || to > today || to.DayNumber - from.DayNumber >= 93 || from.Year < 2000 || query.ZoneId == Guid.Empty)
            throw new ApplicationException("INVALID_ANALYTICS_QUERY", "Use a date range of 1–93 days, from year 2000 through today, and a valid zone ID.", 400);
        if (query.ZoneId is { } zoneId) UseCase.Found(await store.Find<Zone>(zoneId, ct));
        var starts = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(day => calendar.DayStart(from.AddDays(day))).ToArray();
        return new(from, to, calendar.TimeZone.Id, starts[0], calendar.DayStart(to.AddDays(1)), now, query.ZoneId, starts);
    }

    public async Task<AnalyticsOverview> Overview(AnalyticsQuery query, CancellationToken ct)
    {
        var window = await Window(query, ct);
        return new(window, Summarize(await queries.Read(window, ct)));
    }

    public async Task<AnalyticsTrends> Trends(AnalyticsQuery query, CancellationToken ct)
    {
        var window = await Window(query, ct);
        var rows = (await queries.Read(window, ct)).ToLookup(row => row.Day);
        return new(window, Enumerable.Range(0, window.DayStartsUtc.Length)
            .Select(day => new AnalyticsDay(window.From.AddDays(day), Summarize(rows[day]))).ToArray());
    }

    public async Task<AnalyticsZones> Zones(AnalyticsQuery query, CancellationToken ct)
    {
        var window = await Window(query, ct);
        var zones = await store.List<Zone>(z => window.ZoneId == null || z.ZoneId == window.ZoneId, ct);
        var rows = (await queries.Read(window, ct)).ToLookup(row => row.ZoneId);
        return new(window, zones.OrderBy(zone => zone.Name).ThenBy(zone => zone.ZoneId)
            .Select(zone => new AnalyticsZone(zone.ZoneId, zone.Name, Summarize(rows[zone.ZoneId]))).ToArray());
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
