namespace Supermarket.Domain.CheckoutCounters;

public sealed record CheckoutCapacityMeasurement(bool Available, long? QueueCount, int? StaffedCounterCount,
    decimal? QueuePerStaffedCounter, bool NoStaffedCapacity, string? IssueCode);

/// <summary>Consumes observations from all enabled counters in one Checkout Zone, once per counter.</summary>
public static class CheckoutCapacity
{
    public static CheckoutCapacityMeasurement Measure(IReadOnlyList<CheckoutCounterObservation> counters, int maxCounters)
    {
        Rules.Require(maxCounters > 0 && counters.Count <= maxCounters,
            "CHECKOUT_COUNTER_LIMIT", "Configured counter capacity must be positive and cannot be exceeded.");
        Rules.Require(counters.All(c => c.CounterId != Guid.Empty && c.QueueCount is not < 0 && Enum.IsDefined(c.Presence))
            && counters.Select(c => c.CounterId).Distinct().Count() == counters.Count,
            "CHECKOUT_COUNTER_INVALID", "Counters must have unique identities and valid observations.");
        if (counters.Count == 0) return new(false, null, null, null, false, "NO_ENABLED_COUNTERS");
        if (counters.Any(c => c.Presence == CheckoutPresence.Unknown || c.QueueCount is null))
            return new(false, null, null, null, false, "CHECKOUT_COVERAGE_UNAVAILABLE");
        var queued = counters.Sum(c => (long)c.QueueCount!.Value);
        var staffed = counters.Count(c => c.Presence == CheckoutPresence.Present);
        // Zero staffed capacity with demand is explicit, never an Infinity/NaN payload or an arbitrary Alert Level.
        decimal? pressure = staffed > 0 ? (decimal)queued / staffed : queued == 0 ? 0m : null;
        return new(true, queued, staffed, pressure, staffed == 0 && queued > 0, null);
    }
}
