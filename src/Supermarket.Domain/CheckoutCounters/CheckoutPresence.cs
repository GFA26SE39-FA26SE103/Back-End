namespace Supermarket.Domain.CheckoutCounters;

public enum CheckoutPresence { Unknown, Present, Absent }

public sealed record CheckoutPresenceOptions(TimeSpan PresentDuration, TimeSpan AbsentDuration,
    TimeSpan MaxObservationGap, TimeSpan MaxAge, TimeSpan MaxClockSkew, int MaxSources);
public sealed record CheckoutObservationSource(Guid MappingId, Guid CameraId, bool IsEnabled, bool IsQueueSource);
public sealed record CheckoutSourceObservation(Guid MappingId, Guid SessionId, Guid ContinuityId,
    long Sequence, long SourceElapsedMs, DateTime CapturedAt, bool? PersonPresent, int? QueueCount, bool Usable,
    double? MaxWaitSeconds = null, int? LanePeopleCount = null);
public sealed record CheckoutCounterObservation(Guid CounterId, CheckoutPresence Presence, int? QueueCount, bool Disagreement,
    DateTime? ValidUntil = null, double? MaxWaitSeconds = null, int? LanePeopleCount = null);

/// <summary>
/// One configuration-version's bounded, in-memory current state for one counter.
/// The owning service serializes access and replaces this projection on configuration changes.
/// Timers advance only from consecutive observations on each source's own timeline.
/// This does not identify employees, infer transactions, or evaluate MonitoringRules.
/// </summary>
public sealed class CheckoutCounterProjection
{
    private readonly Guid counterId;
    private readonly CheckoutPresenceOptions options;
    private readonly Dictionary<Guid, Source> sources;

    public CheckoutCounterProjection(Guid counterId, IReadOnlyList<CheckoutObservationSource> mappings,
        CheckoutPresenceOptions options)
    {
        Rules.Require(counterId != Guid.Empty, "CHECKOUT_COUNTER_INVALID", "A counter identity is required.");
        Rules.Require(options.MaxSources > 0 && options.PresentDuration > TimeSpan.Zero && options.AbsentDuration > TimeSpan.Zero
            && options.MaxObservationGap > TimeSpan.Zero && options.MaxAge > TimeSpan.Zero && options.MaxClockSkew >= TimeSpan.Zero,
            "CHECKOUT_TIMING_INVALID", "Checkout durations and source capacity must be positive; clock skew cannot be negative.");
        Rules.Require(mappings.Count <= options.MaxSources, "CHECKOUT_SOURCE_LIMIT", "Checkout mapping capacity exceeded.");
        Rules.Require(mappings.All(m => m.MappingId != Guid.Empty && m.CameraId != Guid.Empty)
            && mappings.Select(m => m.MappingId).Distinct().Count() == mappings.Count
            && mappings.Select(m => m.CameraId).Distinct().Count() == mappings.Count,
            "CHECKOUT_MAPPING_INVALID", "Each counter mapping must have a distinct mapping and camera identity.");
        var enabled = mappings.Where(m => m.IsEnabled).ToArray();
        Rules.Require(enabled.Length > 0,
            "CHECKOUT_SOURCE_REQUIRED", "An active counter needs an enabled camera mapping.");
        this.counterId = counterId;
        this.options = options;
        sources = enabled.ToDictionary(m => m.MappingId, m => new Source(m));
    }

    /// <returns>False for replay/out-of-order data; unknown presence never advances a confirmation timer.</returns>
    public bool Observe(CheckoutSourceObservation observation, DateTime now)
    {
        Utc(now);
        Utc(observation.CapturedAt);
        Rules.Require(observation.CapturedAt - now <= options.MaxClockSkew,
            "CHECKOUT_TIMESTAMP_INVALID", "An observation cannot be ahead of the clock beyond the configured tolerance.");
        Rules.Require(sources.ContainsKey(observation.MappingId), "CHECKOUT_SOURCE_UNKNOWN", "Observation is not from an enabled configured mapping.");
        Rules.Require(observation.SessionId != Guid.Empty && observation.ContinuityId != Guid.Empty
            && observation.Sequence > 0 && observation.SourceElapsedMs >= 0 && observation.QueueCount is not < 0
            && observation.LanePeopleCount is not < 0 && !(observation.QueueCount > observation.LanePeopleCount)
            && (observation.MaxWaitSeconds is null || double.IsFinite(observation.MaxWaitSeconds.Value) && observation.MaxWaitSeconds >= 0),
            "CHECKOUT_OBSERVATION_INVALID", "Source identity, sequence, elapsed time and counts must be valid.");
        var source = sources[observation.MappingId];
        var before = source.Last;
        if (before is not null && (observation.CapturedAt < before.CapturedAt
            || observation.SessionId == before.SessionId && observation.Sequence <= before.Sequence
            || observation.SessionId != before.SessionId && observation.CapturedAt <= before.CapturedAt)) return false;

        var continuous = before is not null && before.SessionId == observation.SessionId
            && before.ContinuityId == observation.ContinuityId && before.Sequence + 1 == observation.Sequence
            && observation.SourceElapsedMs >= before.SourceElapsedMs
            && observation.SourceElapsedMs - before.SourceElapsedMs <= options.MaxObservationGap.TotalMilliseconds
            && observation.CapturedAt - before.CapturedAt <= options.MaxObservationGap;
        // A status poll may have invalidated an expired source already. Perform the same
        // invalidation here, so recovery cannot depend on how often the UI polls Read().
        if (!continuous || before is not null && !Fresh(before, now)) source.Invalidate();
        source.Last = observation;
        if (!observation.Usable || !Fresh(observation, now))
        {
            source.Invalidate();
            return true;
        }

        source.QueueCount = observation.QueueCount;
        source.LanePeopleCount = observation.LanePeopleCount;
        source.MaxWaitSeconds = observation.MaxWaitSeconds;
        if (observation.PersonPresent is null)
        {
            // Lane evidence can remain valid when this view cannot resolve person presence
            // at the cashier position. Another configured camera may provide that evidence.
            source.InvalidatePresence();
            return true;
        }
        if (source.Pending != observation.PersonPresent)
        {
            source.Pending = observation.PersonPresent;
            source.PendingSinceMs = observation.SourceElapsedMs;
        }
        var required = observation.PersonPresent.Value ? options.PresentDuration : options.AbsentDuration;
        if (observation.SourceElapsedMs - source.PendingSinceMs >= required.TotalMilliseconds)
            source.Confirmed = observation.PersonPresent.Value ? CheckoutPresence.Present : CheckoutPresence.Absent;
        return true;
    }

    public CheckoutCounterObservation Read(DateTime now)
    {
        Utc(now);
        foreach (var source in sources.Values)
            if (source.Last is null || !Fresh(source.Last, now)) source.Invalidate();
        var present = sources.Values.Any(s => s.Confirmed == CheckoutPresence.Present);
        var absent = sources.Values.Any(s => s.Confirmed == CheckoutPresence.Absent);
        var state = present ? CheckoutPresence.Present
            : sources.Values.All(s => s.Confirmed == CheckoutPresence.Absent) ? CheckoutPresence.Absent : CheckoutPresence.Unknown;
        // Each camera can detect first. Take the strongest current reading per counter, never sum
        // the same physical lane across cameras; the rule/zone episode remains unique downstream.
        var queueSources = sources.Values.Where(s => s.QueueCount is not null && s.Last is not null).ToArray();
        var queueSource = queueSources.OrderByDescending(s => s.QueueCount)
            .ThenByDescending(s => s.Last!.CapturedAt).ThenBy(s => s.Mapping.MappingId).FirstOrDefault();
        var waitSource = queueSources.Where(s => s.MaxWaitSeconds is not null)
            .OrderByDescending(s => s.MaxWaitSeconds).ThenByDescending(s => s.Last!.CapturedAt)
            .ThenBy(s => s.Mapping.MappingId).FirstOrDefault();
        var queueCount = queueSource?.QueueCount;
        var maxWait = waitSource?.MaxWaitSeconds;
        DateTime? presenceAt = state == CheckoutPresence.Present
            ? sources.Values.Where(s => s.Confirmed == CheckoutPresence.Present).Max(s => s.Last!.CapturedAt)
            : state == CheckoutPresence.Absent ? sources.Values.Min(s => s.Last!.CapturedAt) : null;
        var queueAt = queueSource?.Last?.CapturedAt;
        if (queueAt is not null && waitSource?.Last is { } wait && wait.CapturedAt < queueAt)
            queueAt = wait.CapturedAt;
        var validUntil = presenceAt is { } witnessed && queueAt is { } observed
            ? (witnessed < observed ? witnessed : observed) + options.MaxAge
            : (DateTime?)null;
        return new(counterId, state, queueCount, present && absent, validUntil, maxWait, queueSource?.LanePeopleCount);
    }

    /// <summary>Runtime health/transport invalidation is not a negative person detection.</summary>
    public void Unavailable(Guid mappingId)
    {
        Rules.Require(sources.ContainsKey(mappingId), "CHECKOUT_SOURCE_UNKNOWN", "Mapping is not part of this counter configuration.");
        sources[mappingId].Invalidate();
    }

    private bool Fresh(CheckoutSourceObservation value, DateTime now) =>
        value.CapturedAt - now <= options.MaxClockSkew && now - value.CapturedAt <= options.MaxAge;

    private static void Utc(DateTime value) => Rules.Require(value.Kind == DateTimeKind.Utc,
        "CHECKOUT_TIMESTAMP_INVALID", "Checkout observation timestamps and clock must be UTC.");

    private sealed class Source(CheckoutObservationSource mapping)
    {
        public CheckoutObservationSource Mapping { get; } = mapping;
        public CheckoutSourceObservation? Last;
        public bool? Pending;
        public long PendingSinceMs;
        public CheckoutPresence Confirmed;
        public int? QueueCount;
        public int? LanePeopleCount;
        public double? MaxWaitSeconds;
        public void InvalidatePresence() { Pending = null; PendingSinceMs = 0; Confirmed = CheckoutPresence.Unknown; }
        public void Invalidate() { InvalidatePresence(); QueueCount = null; LanePeopleCount = null; MaxWaitSeconds = null; }
    }
}
