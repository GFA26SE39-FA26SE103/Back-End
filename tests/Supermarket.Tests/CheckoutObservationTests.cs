using Supermarket.Domain;
using Supermarket.Domain.CheckoutCounters;
using Xunit;

namespace Supermarket.Tests;

public sealed class CheckoutObservationTests
{
    private static readonly DateTime Start = new(2026, 11, 11, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Counter = Guid.NewGuid(), CameraA = Guid.NewGuid(), CameraB = Guid.NewGuid();
    private static readonly Guid SourceA = Guid.NewGuid(), SourceB = Guid.NewGuid(), Session = Guid.NewGuid(), Continuity = Guid.NewGuid();
    private static CheckoutPresenceOptions Options => new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1), 16);
    private static CheckoutCounterProjection World(bool second = false) => new(Counter,
        second ? [new(SourceA, CameraA, true, true), new(SourceB, CameraB, true, false)] : [new(SourceA, CameraA, true, true)], Options);
    private static CheckoutSourceObservation Frame(Guid mapping, int second, bool? present, int? queue = 4,
        Guid? session = null, Guid? continuity = null, long? sequence = null, bool usable = true) =>
        new(mapping, session ?? Session, continuity ?? Continuity, sequence ?? second + 1, second * 1000L,
            Start.AddSeconds(second), present, queue, usable);
    private static void Observe(CheckoutCounterProjection p, int second, bool? present, bool both = false)
    {
        p.Observe(Frame(SourceA, second, present), Start.AddSeconds(second));
        if (both) p.Observe(Frame(SourceB, second, present, 100), Start.AddSeconds(second));
    }

    [Fact]
    public void WaitingTimeComesOnlyFromQueueSourceAndClearsOnFailure()
    {
        var p = World(true);
        p.Observe(Frame(SourceA, 0, true) with { MaxWaitSeconds = 12 }, Start);
        p.Observe(Frame(SourceB, 0, true) with { MaxWaitSeconds = 999 }, Start);
        Assert.Equal(12, p.Read(Start).MaxWaitSeconds);
        p.Unavailable(SourceA);
        Assert.Null(p.Read(Start).MaxWaitSeconds);
    }

    [Theory, InlineData(true, CheckoutPresence.Present), InlineData(false, CheckoutPresence.Absent)]
    public void RequiresFifteenSecondsOfContinuousValidObservations(bool present, CheckoutPresence expected)
    {
        var p = World();
        for (var t = 0; t < 15; t++) Observe(p, t, present);
        Assert.Equal(CheckoutPresence.Unknown, p.Read(Start.AddSeconds(14)).Presence);
        Observe(p, 15, present);
        Assert.Equal(expected, p.Read(Start.AddSeconds(15)).Presence);
    }

    [Fact]
    public void OneConfirmedPresentWinsEvenWithAnotherUnknownSource()
    {
        var p = World(true);
        for (var t = 0; t <= 15; t++) Observe(p, t, true);
        Assert.Equal(CheckoutPresence.Present, p.Read(Start.AddSeconds(15)).Presence);
    }

    [Fact]
    public void AbsenceRequiresAllEnabledSourcesAndQueueUsesOnlySelectedSource()
    {
        var p = World(true);
        for (var t = 0; t <= 15; t++) Observe(p, t, false, true);
        var result = p.Read(Start.AddSeconds(15));
        Assert.Equal(CheckoutPresence.Absent, result.Presence);
        Assert.Equal(4, result.QueueCount);
        Assert.False(result.Disagreement);
    }

    [Fact]
    public void DisagreementIsPresentAndIsReported()
    {
        var p = World(true);
        for (var t = 0; t <= 15; t++)
        {
            p.Observe(Frame(SourceA, t, true), Start.AddSeconds(t));
            p.Observe(Frame(SourceB, t, false), Start.AddSeconds(t));
        }
        Assert.Equal(CheckoutPresence.Present, p.Read(Start.AddSeconds(15)).Presence);
        Assert.True(p.Read(Start.AddSeconds(15)).Disagreement);
    }

    [Fact]
    public void StaleSourcesBecomeUnknownAndDoNotProvideQueue()
    {
        var p = World(); for (var t = 0; t <= 15; t++) Observe(p, t, true);
        var result = p.Read(Start.AddSeconds(19));
        Assert.Equal(CheckoutPresence.Unknown, result.Presence);
        Assert.Null(result.QueueCount);
    }

    [Fact]
    public void InvalidObservationImmediatelyInvalidatesConfirmedPresence()
    {
        var p = World(); for (var t = 0; t <= 15; t++) Observe(p, t, true);
        p.Observe(Frame(SourceA, 16, null, usable: false), Start.AddSeconds(16));
        Assert.Equal(CheckoutPresence.Unknown, p.Read(Start.AddSeconds(16)).Presence);
        for (var t = 17; t <= 31; t++) Observe(p, t, true);
        Assert.Equal(CheckoutPresence.Unknown, p.Read(Start.AddSeconds(31)).Presence);
        Observe(p, 32, true);
        Assert.Equal(CheckoutPresence.Present, p.Read(Start.AddSeconds(32)).Presence);
    }

    [Fact]
    public void GapOrNewContinuityCannotCompleteAnOldTimer()
    {
        var p = World(); Observe(p, 0, true); Observe(p, 15, true);
        Assert.Equal(CheckoutPresence.Unknown, p.Read(Start.AddSeconds(15)).Presence);
        for (var t = 16; t < 30; t++) Observe(p, t, true);
        p.Observe(Frame(SourceA, 30, true, continuity: Guid.NewGuid()), Start.AddSeconds(30));
        Assert.Equal(CheckoutPresence.Unknown, p.Read(Start.AddSeconds(30)).Presence);
    }

    [Fact]
    public void ReplayCannotChangeConfirmedPresenceOrQueue()
    {
        var p = World(); for (var t = 0; t <= 15; t++) Observe(p, t, true);
        Assert.False(p.Observe(Frame(SourceA, 14, false, 99), Start.AddSeconds(15)));
        Assert.Equal(CheckoutPresence.Present, p.Read(Start.AddSeconds(15)).Presence);
        Assert.Equal(4, p.Read(Start.AddSeconds(15)).QueueCount);
    }

    [Fact]
    public void PendingOppositeSignalDoesNotFlickerAndNoSecondDebounceIsApplied()
    {
        var p = World(); for (var t = 0; t <= 15; t++) Observe(p, t, true);
        for (var t = 16; t < 31; t++) Observe(p, t, false);
        Assert.Equal(CheckoutPresence.Present, p.Read(Start.AddSeconds(30)).Presence);
        Observe(p, 31, false);
        Assert.Equal(CheckoutPresence.Absent, p.Read(Start.AddSeconds(31)).Presence);
    }

    [Fact]
    public void DisabledSourcesDoNotBlockAbsenceOrContributeQueue()
    {
        var p = new CheckoutCounterProjection(Counter,
            [new(SourceA, CameraA, true, true), new(SourceB, CameraB, false, false)], Options);
        for (var t = 0; t <= 15; t++) Observe(p, t, false);
        Assert.Equal(CheckoutPresence.Absent, p.Read(Start.AddSeconds(15)).Presence);
    }

    [Fact]
    public void UnknownOrDuplicateMappingsAndMissingQueueSourceAreRejected()
    {
        Assert.Throws<DomainException>(() => new CheckoutCounterProjection(Counter,
            [new(SourceA, CameraA, true, true), new(SourceB, CameraB, true, true)], Options));
        Assert.Throws<DomainException>(() => new CheckoutCounterProjection(Counter,
            [new(SourceA, CameraA, true, false)], Options));
        Assert.Throws<DomainException>(() => World().Observe(Frame(Guid.NewGuid(), 0, true), Start));
    }

    [Fact]
    public void InvalidCountersOrTimingAndCapacityLimitsAreRejected()
    {
        Assert.Throws<DomainException>(() => new CheckoutCounterProjection(Guid.Empty, [new(SourceA, CameraA, true, true)], Options));
        Assert.Throws<DomainException>(() => new CheckoutCounterProjection(Counter,
            [new(SourceA, CameraA, true, true)], Options with { MaxSources = 0 }));
        Assert.Throws<DomainException>(() => World().Observe(Frame(SourceA, 0, true, -1), Start));
        Assert.Throws<DomainException>(() => World().Read(DateTime.SpecifyKind(Start, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void FutureFrameCannotPoisonTheSourceCursor()
    {
        var p = World(); Observe(p, 0, true);
        Assert.Throws<DomainException>(() => p.Observe(Frame(SourceA, 999, true), Start));
        for (var t = 1; t <= 15; t++) Observe(p, t, true);
        Assert.Equal(CheckoutPresence.Present, p.Read(Start.AddSeconds(15)).Presence);
    }

    [Fact]
    public void JustUnderFifteenSecondsDoesNotConfirm()
    {
        var p = World(); for (var t = 0; t <= 14; t++) Observe(p, t, true);
        p.Observe(Frame(SourceA, 15, true) with { SourceElapsedMs = 14900, CapturedAt = Start.AddMilliseconds(14900) }, Start.AddMilliseconds(14900));
        Assert.Equal(CheckoutPresence.Unknown, p.Read(Start.AddMilliseconds(14900)).Presence);
        p.Observe(Frame(SourceA, 15, true, sequence: 17), Start.AddSeconds(15));
        Assert.Equal(CheckoutPresence.Present, p.Read(Start.AddSeconds(15)).Presence);
    }

    [Fact]
    public void ANewSessionOrAProjectionRestartRequiresNewEvidence()
    {
        var p = World(); for (var t = 0; t <= 15; t++) Observe(p, t, true);
        var restarted = Frame(SourceA, 16, true, session: Guid.NewGuid(), sequence: 1) with { SourceElapsedMs = 0 };
        p.Observe(restarted, Start.AddSeconds(16));
        Assert.Equal(CheckoutPresence.Unknown, p.Read(Start.AddSeconds(16)).Presence);
        Assert.Equal(CheckoutPresence.Unknown, World().Read(Start.AddSeconds(16)).Presence);
    }

    [Fact]
    public void ASecondaryCameraCannotTakeOverTheQueueMeasurement()
    {
        var p = World(true);
        for (var t = 0; t <= 15; t++) p.Observe(Frame(SourceB, t, true, 100), Start.AddSeconds(t));
        var c = p.Read(Start.AddSeconds(15));
        Assert.Equal(CheckoutPresence.Present, c.Presence);
        Assert.Null(c.QueueCount);
        Assert.False(CheckoutCapacity.Measure([c], 10).Available);
    }

    [Fact]
    public void RecoveryDoesNotDependOnWhetherStatusWasPolledDuringStaleness()
    {
        var polled = World(); var notPolled = World();
        for (var t = 0; t <= 14; t++) { Observe(polled, t, true); Observe(notPolled, t, true); }
        var now = Start.AddSeconds(18);
        Assert.Equal(CheckoutPresence.Unknown, polled.Read(now).Presence);
        var delayed = Frame(SourceA, 16, true, sequence: 16);
        polled.Observe(delayed, now); notPolled.Observe(delayed, now);
        Assert.Equal(CheckoutPresence.Unknown, polled.Read(now).Presence);
        Assert.Equal(polled.Read(now), notPolled.Read(now));
    }

    [Fact]
    public void ValidQueueAndStaffFromDifferentSourcesCanProvideCoverage()
    {
        var p = World(true);
        for (var t = 0; t <= 15; t++)
        {
            p.Observe(Frame(SourceA, t, null, 4), Start.AddSeconds(t));
            p.Observe(Frame(SourceB, t, true, null), Start.AddSeconds(t));
        }
        var counter = p.Read(Start.AddSeconds(15));
        Assert.Equal(CheckoutPresence.Present, counter.Presence);
        Assert.Equal(4, counter.QueueCount);
        Assert.True(CheckoutCapacity.Measure([counter], 10).Available);
    }

    [Fact]
    public void CapacityCountsCounterOnceAndDoesNotInventZeroForUnknown()
    {
        var p = World(true); for (var t = 0; t <= 15; t++) Observe(p, t, true, true);
        var c = p.Read(Start.AddSeconds(15));
        var metric = CheckoutCapacity.Measure([c], 10);
        Assert.True(metric.Available); Assert.Equal(4L, metric.QueueCount);
        Assert.Equal(1, metric.StaffedCounterCount); Assert.Equal(4m, metric.QueuePerStaffedCounter);
        Assert.Throws<DomainException>(() => CheckoutCapacity.Measure([c, c], 10));
        Assert.False(CheckoutCapacity.Measure([p.Read(Start.AddSeconds(20))], 10).Available);
        Assert.Null(CheckoutCapacity.Measure([p.Read(Start.AddSeconds(20))], 10).QueueCount);
    }

    [Theory, InlineData(0, false), InlineData(3, true)]
    public void ZeroStaffedCounterHasExplicitFiniteCapacityResult(int queue, bool pressure)
    {
        var m = CheckoutCapacity.Measure([new(Counter, CheckoutPresence.Absent, queue, false)], 10);
        Assert.True(m.Available); Assert.Equal(pressure, m.NoStaffedCapacity);
        if (pressure) Assert.Null(m.QueuePerStaffedCounter);
        else Assert.Equal(0m, m.QueuePerStaffedCounter);
    }
}
