using Supermarket.Domain;

namespace Supermarket.Application;

internal static class AnalyticsWindowResolver
{
    internal static async Task<AnalyticsWindow> Resolve(AnalyticsQuery query, ISetupStore store, IClock clock,
        StoreCalendar calendar, CancellationToken ct)
    {
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
}
