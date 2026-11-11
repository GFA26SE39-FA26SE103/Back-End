using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.Monitoring;
using Xunit;

namespace Supermarket.Tests;

public sealed class CheckoutCoordinatorTests
{
    [Fact]
    public async Task MixedCameraSharesOneAiSessionAndWritesBothEventsAfterPresenceAndSustain()
    {
        await using var world = new World();
        for (var second = 0; second < 15; second++) await world.Frame(second);

        var generic = Assert.Single(world.Events);
        Assert.Equal(world.GenericRule.RuleId, generic.RuleId);
        Assert.Equal(world.GenericRule.EventTypeId, generic.EventTypeId);
        Assert.Equal("CRITICAL", generic.Priority);
        Assert.Equal("UNAVAILABLE", world.CheckoutView.State);
        Assert.Equal("UNKNOWN", Assert.Single(world.CheckoutView.Counters).Presence);
        Assert.Null(Assert.Single(world.CheckoutView.Rules).MetricValue);

        await world.Frame(15); // Fifteen consecutive source seconds confirm presence; sustain begins now.
        Assert.Equal("READY", world.CheckoutView.State);
        Assert.Equal("PRESENT", Assert.Single(world.CheckoutView.Counters).Presence);
        Assert.Equal(1, world.CheckoutView.StaffedCounterCount);
        Assert.Equal(8L, world.CheckoutView.QueueCount);
        Assert.Equal(8m, world.CheckoutView.QueuePerStaffedCounter);
        Assert.Equal(0L, Assert.Single(world.CheckoutView.Rules).CriticalProgressMs);
        Assert.Single(world.Events);
        await world.Frame(16);
        Assert.Equal(1000L, Assert.Single(world.CheckoutView.Rules).CriticalProgressMs);
        Assert.Single(world.Events);
        await world.Frame(17); // Explicit two-second MonitoringRule sustain, not just presence confirmation.

        Assert.Equal(2, world.Events.Length);
        var checkout = Assert.Single(world.Events, e => e.RuleId == world.CheckoutRule.RuleId);
        Assert.Equal(world.CheckoutRule.EventTypeId, checkout.EventTypeId);
        Assert.Equal(world.H.Zone.ZoneId, checkout.ZoneId);
        Assert.Equal(world.Snapshot.CameraId, checkout.CameraId);
        Assert.Equal("CRITICAL", checkout.Priority);
        Assert.Equal(8m, checkout.MetricValue);
        Assert.Equal(world.Clock.UtcNow, checkout.DetectedAt);
        Assert.Null(checkout.ClearedAt);
        Assert.Equal(2000L, Assert.Single(world.CheckoutView.Rules).CriticalProgressMs);
        Assert.True(world.Snapshots.GenericChecks > 0);
        Assert.True(world.Snapshots.AggregateChecks > 0);
        Assert.True(world.History.Calls > 0);
        Assert.Equal(2, world.Store.EventAdds);
        Assert.Empty(world.H.Store.Values.OfType<Incident>());

        using (var metadata = JsonDocument.Parse(checkout.MetadataJson!))
        {
            var evidence = metadata.RootElement.GetProperty("checkout");
            Assert.Equal(8L, evidence.GetProperty("queueCount").GetInt64());
            Assert.Equal(1, evidence.GetProperty("staffedCounterCount").GetInt32());
            Assert.Equal(world.Snapshot.CameraId, Assert.Single(evidence.GetProperty("sourceCameraIds").EnumerateArray()).GetGuid());
            Assert.Equal(world.Mapping.MappingId, Assert.Single(evidence.GetProperty("sourceMappingIds").EnumerateArray()).GetGuid());
        }
        using (var metadata = JsonDocument.Parse(generic.MetadataJson!))
            Assert.Equal(world.Ai.Session, metadata.RootElement.GetProperty("sessionId").GetGuid());

        AssertSharedSession(world);
        var eventIds = world.Events.Select(e => e.EventId).Order().ToArray();
        var transactions = world.Store.Transactions;
        var delivered = world.Ai.DeliveredFrames;
        world.Ai.ReplayFrame = true; // Return an already-consumed batch despite the requested cursor.
        await world.Coordinator.Tick(default);
        await world.Coordinator.Tick(default);
        Assert.Equal(delivered + 2, world.Ai.DeliveredFrames);
        Assert.Equal(eventIds, world.Events.Select(e => e.EventId).Order().ToArray());
        Assert.Equal(2, world.Store.EventAdds);
        Assert.Equal(transactions, world.Store.Transactions);
        AssertSharedSession(world);

        await world.Coordinator.Shutdown(default);
        Assert.Equal(1, world.Ai.Stops);
        Assert.False(world.Coordinator.IsMonitoringOwned(world.Snapshot.CameraId));
        Assert.Null(world.Runtime.Get(world.H.Zone.ZoneId));
    }

    [Fact]
    public async Task ProtocolOnePageFailsClosedBeforeMixedCameraCanWriteAnyOperationalEvent()
    {
        await using var world = new World();
        world.Ai.ProtocolVersion = 1;
        await world.Frame(0);

        var camera = Assert.IsType<MonitoringCameraRuntimeView>(world.Coordinator.Get(world.Snapshot.CameraId));
        Assert.Equal("ERROR", camera.State);
        Assert.Equal("CHECKOUT_PROTOCOL_UNSUPPORTED", camera.ErrorCode);
        Assert.Equal("UNAVAILABLE", world.CheckoutView.State);
        Assert.Null(world.CheckoutView.QueueCount);
        Assert.Null(world.CheckoutView.StaffedCounterCount);
        Assert.Null(world.CheckoutView.QueuePerStaffedCounter);
        Assert.Null(Assert.Single(world.CheckoutView.Rules).MetricValue);
        Assert.Empty(world.Events);
        Assert.Equal(0, world.Store.EventAdds);
        Assert.Equal(0, world.Store.Transactions);
        Assert.Equal(0, world.Snapshots.GenericChecks);
        Assert.Equal(0, world.Snapshots.AggregateChecks);
        Assert.Equal("ACTIVE", world.H.Configuration.Status);
        Assert.True(world.Coordinator.IsMonitoringOwned(world.Snapshot.CameraId));
        Assert.Equal(1, world.Ai.Starts);
        Assert.Equal(0, world.Ai.Stops);
    }

    [Fact]
    public async Task UnusableCheckoutCoverageBlocksItsMetricsButKeepsGenericSessionAndRecoversInPlace()
    {
        await using var world = new World();
        world.Usability.IsReady = false;
        for (var second = 0; second <= 17; second++) await world.Frame(second);

        Assert.Equal(world.GenericRule.RuleId, Assert.Single(world.Events).RuleId);
        Assert.Equal("UNAVAILABLE", world.CheckoutView.State);
        Assert.Null(world.CheckoutView.QueueCount);
        Assert.Null(world.CheckoutView.StaffedCounterCount);
        Assert.Null(world.CheckoutView.QueuePerStaffedCounter);
        Assert.False(world.CheckoutView.NoStaffedCapacity);
        var counter = Assert.Single(world.CheckoutView.Counters);
        Assert.Equal("UNKNOWN", counter.Presence);
        Assert.Null(counter.QueueCount);
        Assert.Null(Assert.Single(world.CheckoutView.Rules).MetricValue);
        Assert.Equal(0, world.History.Calls);
        Assert.Equal(0, world.Snapshots.AggregateChecks);
        Assert.True(world.Usability.Calls > 0);
        AssertSharedSession(world);

        world.Usability.IsReady = true;
        for (var second = 18; second < 33; second++) await world.Frame(second);
        Assert.Equal("UNAVAILABLE", world.CheckoutView.State);
        Assert.Single(world.Events); // Unusable time did not count toward the presence timer.
        await world.Frame(33);
        Assert.Equal("READY", world.CheckoutView.State);
        Assert.Equal("PRESENT", Assert.Single(world.CheckoutView.Counters).Presence);
        Assert.Single(world.Events);
        await world.Frame(34);
        Assert.Single(world.Events);
        await world.Frame(35);
        Assert.Equal(2, world.Events.Length);
        Assert.Single(world.Events, e => e.RuleId == world.CheckoutRule.RuleId);
        AssertSharedSession(world);
        Assert.Empty(world.H.Store.Values.OfType<Incident>());
    }

    [Fact]
    public async Task EmptyPageHealthLossImmediatelyInvalidatesPreviouslyReadyCheckoutWithoutStoppingSource()
    {
        await using var world = new World();
        for (var second = 0; second <= 15; second++) await world.Frame(second);
        Assert.Equal("READY", world.CheckoutView.State);
        Assert.Equal("PRESENT", Assert.Single(world.CheckoutView.Counters).Presence);
        Assert.Single(world.Events);
        var delivered = world.Ai.DeliveredFrames;
        var transactions = world.Store.Transactions;
        var usabilityCalls = world.Usability.Calls;

        world.Usability.IsReady = false;
        world.Clock.UtcNow = world.Clock.Epoch.AddSeconds(16); // Still fresh: this must be health invalidation, not expiry.
        await world.Coordinator.Tick(default); // Fake respects the cursor, so this page contains no new observations.

        Assert.Equal(delivered, world.Ai.DeliveredFrames);
        Assert.Equal(usabilityCalls + 1, world.Usability.Calls);
        Assert.Equal("UNAVAILABLE", world.CheckoutView.State);
        Assert.Null(world.CheckoutView.QueueCount);
        Assert.Null(world.CheckoutView.StaffedCounterCount);
        Assert.Null(world.CheckoutView.QueuePerStaffedCounter);
        Assert.Equal("UNKNOWN", Assert.Single(world.CheckoutView.Counters).Presence);
        Assert.Null(Assert.Single(world.CheckoutView.Rules).MetricValue);
        Assert.Single(world.Events);
        Assert.Equal(transactions, world.Store.Transactions);
        AssertSharedSession(world);

        world.Usability.IsReady = true;
        await world.Coordinator.Tick(default);
        Assert.Equal("UNAVAILABLE", world.CheckoutView.State); // A healthy empty page cannot revive invalidated evidence.
        Assert.Equal(transactions, world.Store.Transactions);
        AssertSharedSession(world);
    }

    [Fact]
    public async Task CompletedPagesDrainAndEvaluateFinalObservationBeforeInvalidatingCheckout()
    {
        await using var world = new World();
        for (var second = 0; second <= 15; second++) await world.Frame(second);
        Assert.Equal("READY", world.CheckoutView.State);
        Assert.Single(world.Events);

        world.Ai.State = "COMPLETED";
        world.Ai.NewestSequence = 18; // Terminal source, but two buffered frames remain to be consumed.
        await world.Frame(16); // Sequence 17 is not the end of the terminal buffer.
        Assert.Equal("READY", world.CheckoutView.State);
        Assert.Equal(1000L, Assert.Single(world.CheckoutView.Rules).CriticalProgressMs);
        Assert.Single(world.Events);
        AssertSharedSession(world);

        await world.Frame(17); // Sequence 18 completes sustain; evaluate it before retiring source coverage.
        var checkout = Assert.Single(world.Events, e => e.RuleId == world.CheckoutRule.RuleId);
        Assert.Equal(2, world.Events.Length);
        Assert.Equal("CRITICAL", checkout.Priority);
        Assert.Equal(8m, checkout.MetricValue);
        Assert.Equal(world.Clock.UtcNow, checkout.DetectedAt);
        Assert.Null(checkout.ClearedAt);
        Assert.Equal("UNAVAILABLE", world.CheckoutView.State);
        Assert.Null(world.CheckoutView.QueueCount);
        Assert.Null(world.CheckoutView.StaffedCounterCount);
        Assert.Null(Assert.Single(world.CheckoutView.Rules).MetricValue);
        Assert.Equal("UNKNOWN", Assert.Single(world.CheckoutView.Counters).Presence);
        var camera = Assert.IsType<MonitoringCameraRuntimeView>(world.Coordinator.Get(world.Snapshot.CameraId));
        Assert.Equal("COMPLETED", camera.State);
        Assert.Equal(8, Assert.Single(camera.Zones).QueueCount); // Generic final-frame display semantics remain intact.
        Assert.Equal(world.Ai.Session, camera.SessionId);
        Assert.Equal(1, world.Ai.Starts);
        Assert.Equal(0, world.Ai.Stops);
        Assert.True(world.Coordinator.IsMonitoringOwned(world.Snapshot.CameraId));

        var transactions = world.Store.Transactions;
        await world.Coordinator.Tick(default); // Completed, drained, empty poll must not recreate the terminal event.
        Assert.Equal(2, world.Events.Length);
        Assert.Equal(2, world.Store.EventAdds);
        Assert.Equal(transactions, world.Store.Transactions);
        Assert.Null(checkout.ClearedAt);
        Assert.Equal("UNAVAILABLE", world.CheckoutView.State);
    }

    private static void AssertSharedSession(World world)
    {
        var camera = Assert.IsType<MonitoringCameraRuntimeView>(world.Coordinator.Get(world.Snapshot.CameraId));
        Assert.Equal("RUNNING", camera.State);
        Assert.Equal(world.Ai.Session, camera.SessionId);
        Assert.Equal("ACTIVE", world.H.Configuration.Status);
        Assert.True(world.Coordinator.IsMonitoringOwned(world.Snapshot.CameraId));
        Assert.Equal(1, world.Ai.Starts);
        Assert.Equal(0, world.Ai.Stops);
        var started = Assert.Single(world.Ai.StartedSnapshots);
        Assert.Same(world.Snapshot, started);
        Assert.Single(started.Zones);
        Assert.Single(started.CheckoutMappings!);
        Assert.Single(world.Ai.ReadOwners.Distinct());
        Assert.Equal(world.Ai.StartOwner, world.Ai.ReadOwners[0]);
        Assert.All(world.Ai.ReadSessions, session => Assert.Equal(world.Ai.Session, session));
    }

    private sealed class World : IAsyncDisposable
    {
        internal MonitoringSetupTests.Harness H { get; } = new();
        internal Clock Clock { get; } = new();
        internal TrackingStore Store { get; }
        internal SnapshotReader Snapshots { get; }
        internal AiFake Ai { get; }
        internal Usability Usability { get; } = new();
        internal History History { get; } = new();
        internal MonitoringRuleSnapshot GenericRule { get; }
        internal MonitoringRuleSnapshot CheckoutRule { get; }
        internal CheckoutMappingSnapshot Mapping { get; }
        internal MonitoringCameraSnapshot Snapshot { get; }
        internal CheckoutMonitoringRuntime Runtime { get; }
        internal MonitoringCameraCoordinator Coordinator { get; }
        internal ServiceProvider Services { get; }
        internal OperationalEvent[] Events => H.Store.Values.OfType<OperationalEvent>().ToArray();
        internal CheckoutZoneRuntimeView CheckoutView => Assert.IsType<CheckoutZoneRuntimeView>(Runtime.Get(H.Zone.ZoneId));

        internal World()
        {
            H.Configuration.Status = "ACTIVE";
            H.Zone.ZoneType = "CHECKOUT";
            H.AddRule();
            H.AddRule("CHECKOUT_CAPACITY_ISSUE", "CHECKOUT_CAPACITY", "PEOPLE_PER_STAFFED_COUNTER");
            var generic = H.Store.Values.OfType<MonitoringRule>().First();
            var checkout = H.Store.Values.OfType<MonitoringRule>().Last();
            generic.SustainSec = checkout.SustainSec = 2;
            checkout.WarningThreshold = 4;
            checkout.CriticalThreshold = 7;
            checkout.ParametersJson = "{\"minQueue\":1,\"noStaffedAlertLevel\":\"CRITICAL\"}";
            GenericRule = Rule(generic, "LONG_QUEUE", "QUEUE_LENGTH");
            CheckoutRule = Rule(checkout, "CHECKOUT_CAPACITY", "CHECKOUT_CAPACITY");
            Point[] roi = [new(0, 0), new(1, 0), new(0, 1)];
            Mapping = new(Guid.NewGuid(), H.Camera.CameraId, Guid.NewGuid(), H.Zone.ZoneId, H.Zone.Name,
                H.Configuration.ConfigId, H.Configuration.UpdatedAt, Clock.UtcNow, roi, roi, true, .5m, [CheckoutRule]);
            var zone = new MonitoringZoneSnapshot(H.Zone.ZoneId, H.Zone.Name, H.Configuration.ConfigId,
                H.Configuration.UpdatedAt, roi, .5m, [GenericRule]);
            Snapshot = new(H.Camera.CameraId, H.Connection, "mixed-checkout-coordinator", [zone], CheckoutMappings: [Mapping]);
            Store = new(H.Store);
            Snapshots = new(Snapshot, H.Configuration, Store);
            Ai = new(Clock);
            var services = new ServiceCollection();
            services.AddSingleton<ISetupStore>(Store);
            services.AddSingleton<IClock>(Clock);
            services.AddSingleton<IMonitoringSnapshotReader>(Snapshots);
            services.AddSingleton<IAiMonitoringClient>(Ai);
            services.AddSingleton<IMonitoringIncidentQueries>(new Queries());
            services.AddSingleton<ICheckoutCameraUsability>(Usability);
            services.AddSingleton<ICheckoutEventHistory>(History);
            services.AddScoped<MonitoringIncidentWriter>();
            Services = services.BuildServiceProvider();
            Runtime = new(Options.Create(new CheckoutMonitoringOptions { AiContractEnabled = true }), Clock);
            Coordinator = new(Services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new MonitoringWorkerOptions()), Clock, Runtime);
        }

        private static MonitoringRuleSnapshot Rule(MonitoringRule rule, string code, string mode) => new(rule.RuleId,
            null, code, code, mode, rule.ThresholdUnit, rule.WarningThreshold, rule.CriticalThreshold,
            rule.SustainSec, rule.CooldownSec, rule.EventTypeId, code, rule.ClearDurationSec, rule.OutcomeType, rule.ParametersJson);

        internal async Task Frame(int second)
        {
            if (second is < 0 or > 63) throw new ArgumentOutOfRangeException(nameof(second));
            Clock.UtcNow = Clock.Epoch.AddSeconds(second);
            var zone = Snapshot.Zones[0];
            Ai.Frame = new(Ai.ProtocolVersion, Ai.Session, Ai.Continuity, second + 1, Clock.UtcNow, second * 1000L,
                Snapshot.Fingerprint, [new(zone.ZoneId, zone.ConfigId, zone.ConfigVersion, 8, 8)],
                Ai.ProtocolVersion == 2 ? [new(Mapping.MappingId, Mapping.CounterId, Mapping.ConfigId,
                    Mapping.ConfigVersion, Mapping.MappingVersion, true, 8, true)] : null);
            await Coordinator.Tick(default);
        }

        public async ValueTask DisposeAsync()
        {
            await Coordinator.Shutdown(default);
            await Services.DisposeAsync();
        }
    }

    private sealed class Clock : IClock
    {
        internal DateTime Epoch { get; } = new(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);
        public DateTime UtcNow { get; set; } = new(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);
    }

    private sealed class AiFake(Clock clock) : IAiMonitoringClient
    {
        internal Guid Session { get; } = Guid.NewGuid();
        internal Guid Continuity { get; } = Guid.NewGuid();
        internal Guid StartOwner { get; private set; }
        internal int ProtocolVersion { get; set; } = 2;
        internal int Starts { get; private set; }
        internal int Stops { get; private set; }
        internal int DeliveredFrames { get; private set; }
        internal string State { get; set; } = "LIVE";
        internal long? NewestSequence { get; set; }
        internal bool ReplayFrame { get; set; }
        internal AiMeasurementBatch? Frame { get; set; }
        internal List<MonitoringCameraSnapshot> StartedSnapshots { get; } = [];
        internal List<Guid> ReadOwners { get; } = [];
        internal List<Guid?> ReadSessions { get; } = [];

        public Task<AiPreviewStatusView> Start(MonitoringCameraSnapshot snapshot, Guid ownerId, CancellationToken ct)
        {
            Starts++;
            StartOwner = ownerId;
            StartedSnapshots.Add(snapshot);
            return Task.FromResult(new AiPreviewStatusView(snapshot.CameraId, "LIVE", null, clock.UtcNow, 0, null,
                Session, "MONITORING", snapshot.Fingerprint));
        }
        public Task<AiMeasurementPage> Read(Guid cameraId, Guid ownerId, Guid? afterSessionId, long afterSequence, int limit, CancellationToken ct)
        {
            Assert.Equal(Assert.Single(StartedSnapshots).CameraId, cameraId);
            Assert.Equal(StartOwner, ownerId);
            Assert.InRange(limit, 1, 64);
            ReadOwners.Add(ownerId);
            ReadSessions.Add(afterSessionId);
            var batches = Frame is { } frame && (ReplayFrame || frame.Sequence > afterSequence)
                ? new[] { frame } : [];
            DeliveredFrames += batches.Length;
            return Task.FromResult(new AiMeasurementPage(ProtocolVersion, Session, 1, NewestSequence ?? Frame?.Sequence ?? 0, false, batches, State, null));
        }
        public Task Stop(Guid cameraId, Guid ownerId, CancellationToken ct)
        {
            Assert.Equal(StartOwner, ownerId);
            Stops++;
            return Task.CompletedTask;
        }
    }

    private sealed class SnapshotReader(MonitoringCameraSnapshot snapshot, MonitoringConfiguration configuration, TrackingStore store) : IMonitoringSnapshotReader
    {
        internal int GenericChecks { get; private set; }
        internal int AggregateChecks { get; private set; }
        public Task<MonitoringCameraSnapshot[]> ReadAll(CancellationToken ct) => Task.FromResult(new[] { snapshot });
        public Task<bool> IsCurrent(MonitoringCameraSnapshot candidate, Guid zoneId, CancellationToken ct)
        {
            Assert.True(store.InTransaction);
            GenericChecks++;
            return Task.FromResult(configuration.Status == "ACTIVE" && candidate == snapshot && snapshot.Zones.Any(z => z.ZoneId == zoneId));
        }
        public Task<bool> IsCurrentAggregate(MonitoringCameraSnapshot[] sources, Guid zoneId, CancellationToken ct)
        {
            Assert.True(store.InTransaction);
            AggregateChecks++;
            return Task.FromResult(configuration.Status == "ACTIVE" && sources.Length == 1 && sources[0] == snapshot
                && snapshot.CheckoutMappings!.Any(m => m.ZoneId == zoneId));
        }
    }

    private sealed class TrackingStore(MonitoringSetupTests.MemoryStore inner) : ISetupStore
    {
        internal bool InTransaction { get; private set; }
        internal int Transactions { get; private set; }
        internal int EventAdds { get; private set; }
        public Task<T?> Find<T>(Guid id, CancellationToken ct = default) where T : Entity, new() => inner.Find<T>(id, ct);
        public Task<List<T>> List<T>(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) where T : Entity, new() => inner.List(filter, ct);
        public Task Add<T>(T entity, CancellationToken ct = default) where T : Entity, new()
        {
            Assert.True(InTransaction);
            if (entity is OperationalEvent) EventAdds++;
            return inner.Add(entity, ct);
        }
        public Task Update<T>(T entity, CancellationToken ct = default) where T : Entity, new() => inner.Update(entity, ct);
        public Task Remove<T>(T entity, CancellationToken ct = default) where T : Entity, new() => inner.Remove(entity, ct);
        public async Task<T> Transaction<T>(Func<Task<T>> work, CancellationToken ct = default)
        {
            Transactions++;
            InTransaction = true;
            try { return await inner.Transaction(work, ct); }
            finally { InTransaction = false; }
        }
    }

    private sealed class Usability : ICheckoutCameraUsability
    {
        internal bool IsReady { get; set; } = true;
        internal int Calls { get; private set; }
        public Task<bool> Ready(Guid cameraId, CancellationToken ct) { Calls++; return Task.FromResult(IsReady); }
    }
    private sealed class History : ICheckoutEventHistory
    {
        internal int Calls { get; private set; }
        public Task<DateTime?> LatestClearedAt(Guid ruleId, Guid zoneId, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<DateTime?>(null);
        }
    }
    private sealed class Queries : IMonitoringIncidentQueries
    {
        public Task EnsureSchema(CancellationToken ct) => Task.CompletedTask;
        public Task<DateTime?> LatestEndedAt(Guid zoneId, Guid typeId, CancellationToken ct) => Task.FromResult<DateTime?>(null);
        public Task<IncidentFeedView> PageForCamera(Guid cameraId, IncidentFeedQuery query, CancellationToken ct) => throw new NotSupportedException();
    }
}
