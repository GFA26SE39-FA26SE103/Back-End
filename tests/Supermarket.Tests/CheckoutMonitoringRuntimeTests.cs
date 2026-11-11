using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.Monitoring;
using Xunit;

namespace Supermarket.Tests;

public sealed class CheckoutMonitoringRuntimeTests
{
    [Fact]
    public async Task AggregateCreatesOneEventLocksItsOriginAndStalenessDoesNotClearIt()
    {
        using var world = new World();
        for (var t = 0; t <= 16; t++) await world.Frame(t, 8);
        var ev = Assert.Single(world.Store.Values.OfType<OperationalEvent>());
        Assert.Equal(world.Second.CameraId, ev.CameraId);
        Assert.Equal("CRITICAL", ev.Priority); Assert.Equal(8m, ev.MetricValue);
        Assert.Equal(8L, world.Runtime.Get(world.ZoneId)!.QueueCount);
        Assert.Contains("sourceCameraIds", ev.MetadataJson);
        for (var t = 17; t <= 20; t++) await world.Frame(t, 8, includeSecond: false);
        Assert.Equal(world.Second.CameraId, Assert.Single(world.Store.Values.OfType<OperationalEvent>()).CameraId);
        world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(4);
        await world.Runtime.Evaluate(world.Services, default);
        var runtime = world.Runtime.Get(world.ZoneId)!;
        Assert.Equal("UNAVAILABLE", runtime.State); Assert.Null(runtime.QueueCount);
        Assert.Null(Assert.Single(runtime.Rules).MetricValue);
        Assert.Null(ev.ClearedAt); Assert.Empty(world.Store.Values.OfType<Incident>());
    }

    [Fact]
    public async Task RecoveryClearsOnceAndCooldownPreventsANewEpisode()
    {
        using var world = new World();
        for (var t = 0; t <= 16; t++) await world.Frame(t, 8);
        await world.Frame(17, 0); await world.Frame(18, 0);
        Assert.NotNull(Assert.Single(world.Store.Values.OfType<OperationalEvent>()).ClearedAt);
        await world.Frame(19, 8); await world.Frame(20, 8);
        Assert.Single(world.Store.Values.OfType<OperationalEvent>());
        for (var t = 21; t <= 29; t++) await world.Frame(t, 8);
        Assert.Equal(2, world.Store.Values.OfType<OperationalEvent>().Count());
    }

    [Fact]
    public void WrongMappingVersionIsRejectedAndRemovedConfigurationEvictsState()
    {
        using var world = new World();
        var batch = world.Batch(world.First, 0, 2);
        var wrong = batch.Checkouts![0] with { MappingVersion = world.Clock.UtcNow.AddSeconds(1) };
        Assert.Throws<Supermarket.Application.ApplicationException>(() => world.Runtime.Observe(world.First, batch with { Checkouts = [wrong] }, true));
        world.Runtime.Reconcile([]);
        Assert.Null(world.Runtime.Get(world.ZoneId));
    }

    [Fact]
    public async Task SourceFailureWithoutANewValidObservationDoesNotAdvanceSustain()
    {
        using var world=new World();
        for(var t=0;t<=15;t++) await world.Frame(t,8);
        Assert.Empty(world.Store.Values.OfType<OperationalEvent>());
        world.Clock.UtcNow=world.Clock.UtcNow.AddSeconds(1);
        world.Runtime.Unavailable(world.Second.CameraId);
        await world.Runtime.Evaluate(world.Services,default);
        Assert.Empty(world.Store.Values.OfType<OperationalEvent>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EvidenceExpiringDuringPersistenceCannotCreateAnEvent(bool duringHistory)
    {
        using var world = new World();
        for (var t = 0; t <= 15; t++) await world.Frame(t, 8);
        if (duringHistory) world.History.BeforeRead = () => world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(4);
        else world.Snapshots.BeforeCheck = () => world.Clock.UtcNow = world.Clock.UtcNow.AddSeconds(4);
        await world.Frame(16, 8);
        Assert.Empty(world.Store.Values.OfType<OperationalEvent>());
        Assert.Equal("UNAVAILABLE", world.Runtime.Get(world.ZoneId)!.State);
        Assert.Equal("CHECKOUT_OBSERVATION_STALE", world.Runtime.Get(world.ZoneId)!.IssueCode);
    }

    private sealed class World : IDisposable
    {
        public MonitoringSetupTests.MemoryStore Store { get; } = new();
        public Clock Clock { get; } = new();
        public Guid ZoneId { get; } = Guid.NewGuid();
        public MonitoringCameraSnapshot First { get; }
        public MonitoringCameraSnapshot Second { get; }
        public CheckoutMonitoringRuntime Runtime { get; }
        public ServiceProvider Services { get; }
        public History History { get; }
        public Snapshots Snapshots { get; }
        private readonly Guid firstSession = Guid.NewGuid(), secondSession = Guid.NewGuid(), continuity = Guid.NewGuid();
        public World()
        {
            var config = Guid.NewGuid(); var counter = Guid.NewGuid(); var type = Guid.NewGuid();
            Point[] roi = [new(0, 0), new(1, 0), new(0, 1)];
            var rule = new MonitoringRuleSnapshot(Guid.NewGuid(), null, "CHECKOUT_CAPACITY", "Capacity", "CHECKOUT_CAPACITY",
                "PEOPLE_PER_STAFFED_COUNTER", 4, 7, 1, 10, type, "CHECKOUT_CAPACITY", 1,
                ParametersJson: "{\"minQueue\":1,\"noStaffedAlertLevel\":\"CRITICAL\"}");
            MonitoringCameraSnapshot Camera(bool primary)
            {
                var id = Guid.NewGuid();
                return new(id, new CameraConnection { CameraId = id, SourceType = "LIVE", Protocol = "HTTP", StreamUri = "http://camera.test/video" },
                    primary ? "first" : "second", [], CheckoutMappings: [new(Guid.NewGuid(), id, counter, ZoneId, "Checkout", config,
                        Clock.UtcNow, Clock.UtcNow, roi, roi, primary, .5m, [rule])]);
            }
            First = Camera(true); Second = Camera(false);
            var collection = new ServiceCollection();
            collection.AddSingleton<ISetupStore>(Store); collection.AddSingleton<IClock>(Clock);
            Snapshots = new([First, Second]);
            History = new(Store);
            collection.AddSingleton<IMonitoringSnapshotReader>(Snapshots);
            collection.AddSingleton<IMonitoringIncidentQueries>(new Queries());
            collection.AddSingleton<ICheckoutEventHistory>(History);
            collection.AddScoped<MonitoringIncidentWriter>();
            Services = collection.BuildServiceProvider();
            Runtime = new(Options.Create(new CheckoutMonitoringOptions { AiContractEnabled = true }), Clock);
            Runtime.Reconcile([First, Second]);
        }
        public AiMeasurementBatch Batch(MonitoringCameraSnapshot camera, int second, int queue)
        {
            var m = camera.CheckoutMappings![0];
            return new(2, camera == First ? firstSession : secondSession, continuity, second + 1, Clock.UtcNow, second * 1000L,
                camera.Fingerprint, [], [new(m.MappingId, m.CounterId, m.ConfigId, m.ConfigVersion, m.MappingVersion, true, m.IsQueueSource ? queue : null, true)]);
        }
        public async Task Frame(int second, int queue, bool includeSecond = true)
        {
            Clock.UtcNow = new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc).AddSeconds(second);
            Runtime.Observe(First, Batch(First, second, queue), true);
            if (includeSecond) Runtime.Observe(Second, Batch(Second, second, queue), true);
            await Runtime.Evaluate(Services, default);
        }
        public void Dispose() => Services.Dispose();
    }
    private sealed class Clock : IClock { public DateTime UtcNow { get; set; } = new(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc); }
    private sealed class Snapshots(MonitoringCameraSnapshot[] snapshots) : IMonitoringSnapshotReader
    {
        public Action? BeforeCheck;
        public Task<MonitoringCameraSnapshot[]> ReadAll(CancellationToken ct) => Task.FromResult(snapshots);
        public Task<bool> IsCurrent(MonitoringCameraSnapshot s, Guid zoneId, CancellationToken ct)
        { BeforeCheck?.Invoke(); return Task.FromResult(snapshots.Contains(s)); }
    }
    private sealed class History(MonitoringSetupTests.MemoryStore store) : ICheckoutEventHistory
    {
        public Action? BeforeRead;
        public Task<DateTime?> LatestClearedAt(Guid rule, Guid zone, CancellationToken ct)
        { BeforeRead?.Invoke(); return Task.FromResult(store.Values.OfType<OperationalEvent>().Where(e => e.RuleId == rule && e.ZoneId == zone).Max(e => e.ClearedAt)); }
    }
    private sealed class Queries : IMonitoringIncidentQueries
    {
        public Task EnsureSchema(CancellationToken ct) => Task.CompletedTask;
        public Task<DateTime?> LatestEndedAt(Guid zone, Guid type, CancellationToken ct) => Task.FromResult<DateTime?>(null);
        public Task<IncidentFeedView> PageForCamera(Guid camera, IncidentFeedQuery q, CancellationToken ct) => throw new NotSupportedException();
    }
}
