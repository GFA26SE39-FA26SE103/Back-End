using System.Linq.Expressions;
using System.Text.Json;
using Supermarket.Application;
using Supermarket.Domain;
using Xunit;

namespace Supermarket.Tests;

public sealed class CheckoutSnapshotTests
{
    [Fact]
    public async Task CheckoutOnlyStartsWithoutGenericMappingAndPropagatesRuleParameters()
    {
        var f = new CheckoutFixture();
        var snapshot = Assert.Single(await f.Reader().ReadAll(default));
        Assert.Null(snapshot.IssueCode);
        Assert.Empty(snapshot.Zones);
        var mapping = Assert.Single(snapshot.CheckoutMappings!);
        Assert.Equal(f.Mapping.MappingId, mapping.MappingId);
        Assert.Equal(f.Counter.CheckoutCounterId, mapping.CounterId);
        Assert.Equal(f.H.Zone.ZoneId, mapping.ZoneId);
        Assert.Equal(f.H.Zone.Name, mapping.ZoneName);
        Assert.Equal(f.Mapping.UpdatedAt, mapping.MappingVersion);
        Assert.Equal(f.H.Configuration.UpdatedAt, mapping.ConfigVersion);
        Assert.Equal(f.Rule.ParametersJson, Assert.Single(mapping.Rules).ParametersJson);
        Assert.Equal(3, mapping.LaneRoi.Length);
        Assert.Equal(3, mapping.StaffRoi.Length);
        Assert.True(await f.Reader().IsCurrent(snapshot, f.H.Zone.ZoneId, default));
    }

    [Fact]
    public async Task GenericOnlyNeverReadsCheckoutTablesEvenWithContractEnabled()
    {
        var f = new CheckoutFixture();
        f.Store.Values.Remove(f.Rule);
        f.H.AddRule();
        f.Store.Values.Add(f.GenericMapping);
        f.Store.RejectCheckoutReads = true;
        var snapshot = Assert.Single(await f.Reader().ReadAll(default));
        Assert.Single(snapshot.Zones);
        Assert.Empty(snapshot.CheckoutMappings ?? []);
        Assert.Equal(0, f.Store.CheckoutReads);
        Assert.Equal(0, f.Schema.Calls);
    }

    [Fact]
    public async Task DisabledCheckoutRuleDoesNotReadCheckoutTables()
    {
        var f = new CheckoutFixture();
        f.Rule.Enabled = false;
        f.H.AddRule();
        f.Store.Values.Add(f.GenericMapping);
        f.Store.RejectCheckoutReads = true;
        Assert.Null(Assert.Single(await f.Reader().ReadAll(default)).IssueCode);
        Assert.Equal(0, f.Store.CheckoutReads);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task GenericOnlyConfigurationsAreNotLimitedByCheckoutZoneCaps(int checkoutLimit)
    {
        var f = new CheckoutFixture();
        f.Store.Values.Remove(f.Rule);
        f.H.AddRule();
        f.Store.Values.Add(f.GenericMapping);
        f.Options.MaxZones = checkoutLimit;
        f.Store.RejectCheckoutReads = true;
        for (var i = 0; i < 128; i++)
        {
            var zone = new Zone { ZoneId = Guid.NewGuid(), FloorId = f.H.Zone.FloorId, Status = "ACTIVE" };
            f.Store.Values.AddRange([zone, new MonitoringConfiguration { ConfigId = Guid.NewGuid(), ZoneId = zone.ZoneId, Status = "ACTIVE" }]);
        }
        Assert.Null(Assert.Single(await f.Reader().ReadAll(default)).IssueCode);
        Assert.Equal(0, f.Schema.Calls);
        Assert.Equal(0, f.Store.CheckoutReads);
    }

    [Fact]
    public async Task EnabledCheckoutZonesStillRespectCheckoutZoneLimit()
    {
        var f = new CheckoutFixture();
        var second = new CheckoutFixture();
        f.Store.Values.AddRange(second.Store.Values);
        f.Options.MaxZones = 1;
        var error = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() => f.Reader().ReadAll(default));
        Assert.Equal("CHECKOUT_LIMIT_EXCEEDED", error.Code);
    }

    [Fact]
    public async Task MixedRulesKeepGenericAndCheckoutCameraCoverageIndependent()
    {
        var f = new CheckoutFixture();
        f.H.AddRule();
        var camera = f.AddSource(queueSource: false);
        f.GenericMapping.CameraId = camera.CameraId;
        f.Store.Values.Add(f.GenericMapping);
        var snapshots = await f.Reader().ReadAll(default);
        Assert.Equal(2, snapshots.Length);
        Assert.All(snapshots, s => Assert.Null(s.IssueCode));
        var generic = Assert.Single(snapshots.Single(s => s.CameraId == camera.CameraId).Zones);
        Assert.Equal("LONG_QUEUE", Assert.Single(generic.Rules).EventTypeCode);
        Assert.All(snapshots.SelectMany(s => s.CheckoutMappings!), m =>
            Assert.Equal("CHECKOUT_CAPACITY", Assert.Single(m.Rules).EventTypeCode));
        Assert.Empty(snapshots.Single(s => s.CameraId == f.H.Camera.CameraId).Zones);
    }

    [Fact]
    public async Task MixedRulesWithoutGenericMappingBlockCheckoutSources()
    {
        var f = new CheckoutFixture();
        f.H.AddRule();
        Assert.NotNull(Assert.Single(await f.Reader().ReadAll(default)).IssueCode);
    }

    [Fact]
    public async Task MultipleCheckoutSourcesAreNotGenericSourceAmbiguity()
    {
        var f = new CheckoutFixture();
        f.AddSource(queueSource: false);
        var snapshots = await f.Reader().ReadAll(default);
        Assert.Equal(2, snapshots.Length);
        Assert.All(snapshots, s => Assert.Null(s.IssueCode));
    }

    [Fact]
    public async Task ContractFlagDefaultsOffAndBlocksSavedActiveCheckout()
    {
        var f = new CheckoutFixture();
        var reader = new MonitoringSnapshotReader(f.Store, checkoutSchema: f.Schema, checkoutQueries: f.Queries);
        var snapshot = Assert.Single(await reader.ReadAll(default));
        Assert.Equal("CHECKOUT_AI_CONTRACT_DISABLED", snapshot.IssueCode);
        Assert.False(await reader.IsCurrent(snapshot, f.H.Zone.ZoneId, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingSchemaFailsClosedBeforeAnyCheckoutEntityRead(bool omitGate)
    {
        var f = new CheckoutFixture();
        f.Schema.IsReady = false;
        f.Store.RejectCheckoutReads = true;
        var reader = omitGate ? new MonitoringSnapshotReader(f.Store, f.Options) : f.Reader();
        var error = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() => reader.ReadAll(default));
        Assert.Equal("CHECKOUT_SCHEMA_NOT_READY", error.Code);
        Assert.Equal(0, f.Store.CheckoutReads);
    }

    [Fact]
    public async Task IsCurrentRejectsRemovedCheckoutZoneAndChangedRemoteSource()
    {
        var f = new CheckoutFixture();
        var other = f.AddSource(queueSource: false);
        var reader = f.Reader();
        var snapshot = (await reader.ReadAll(default)).Single(s => s.CameraId == f.H.Camera.CameraId);
        Assert.False(await reader.IsCurrent(snapshot, Guid.NewGuid(), default));
        f.Store.Values.OfType<CameraConnection>().Single(c => c.CameraId == other.CameraId).UpdatedAt = DateTime.UtcNow;
        Assert.False(await reader.IsCurrent(snapshot, f.H.Zone.ZoneId, default));
        f.Mapping.IsEnabled = false;
        Assert.False(await reader.IsCurrent(snapshot, f.H.Zone.ZoneId, default));
    }

    [Fact]
    public async Task GenericSnapshotStillCarriesConfiguredParameters()
    {
        var h = new MonitoringSetupTests.Harness();
        h.AddRule("OVERCROWDING_CONGESTION", "CROWD_DENSITY", "PEOPLE");
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        rule.ParametersJson = "{\"measurementMode\":\"PEOPLE_COUNT\"}";
        h.Configuration.Status = "ACTIVE";
        var snapshot = Assert.Single(await new MonitoringSnapshotReader(h.Store).ReadAll(default));
        Assert.Null(snapshot.IssueCode);
        Assert.Equal(rule.ParametersJson, Assert.Single(Assert.Single(snapshot.Zones).Rules).ParametersJson);
    }

    [Fact]
    public async Task AggregateCurrentReadsOnceAndChecksAllZoneContributors()
    {
        var f = new CheckoutFixture();
        f.AddSource(queueSource: false);
        var reader = f.Reader();
        var snapshots = await reader.ReadAll(default);
        var before = f.Store.ConfigurationReads;
        Assert.True(await reader.IsCurrentAggregate(snapshots, f.H.Zone.ZoneId, default));
        Assert.Equal(before + 1, f.Store.ConfigurationReads);
        Assert.False(await reader.IsCurrentAggregate([snapshots[0]], f.H.Zone.ZoneId, default));
        Assert.False(await reader.IsCurrentAggregate([], f.H.Zone.ZoneId, default));
        Assert.False(await reader.IsCurrentAggregate(snapshots, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task AggregateCurrentRejectsNewContributorRemovedMappingAndStaleRemoteCamera()
    {
        var f = new CheckoutFixture();
        var reader = f.Reader();
        var initial = await reader.ReadAll(default);
        var other = f.AddSource(queueSource: false);
        Assert.False(await reader.IsCurrentAggregate(initial, f.H.Zone.ZoneId, default));
        var complete = await reader.ReadAll(default);
        f.Store.Values.OfType<CameraCheckoutMapping>().Single(m => m.CameraId == other.CameraId).IsEnabled = false;
        Assert.False(await reader.IsCurrentAggregate(complete, f.H.Zone.ZoneId, default));
        var remaining = await reader.ReadAll(default);
        f.H.Connection.IsEnabled = false;
        Assert.False(await reader.IsCurrentAggregate(remaining, f.H.Zone.ZoneId, default));
    }

    [Fact]
    public async Task AggregateCurrentRejectsPartialMappingSetEvenWithCopiedCurrentFingerprint()
    {
        var f = new CheckoutFixture();
        var counter = new CheckoutCounter { CheckoutCounterId = Guid.NewGuid(), ZoneId = f.H.Zone.ZoneId, IsEnabled = true };
        var mapping = new CameraCheckoutMapping { MappingId = Guid.NewGuid(), CameraId = f.H.Camera.CameraId,
            CheckoutCounterId = counter.CheckoutCounterId, IsEnabled = true, IsQueueSource = true,
            LaneRoi = CheckoutFixture.Polygon, StaffRoi = CheckoutFixture.Polygon };
        f.Store.Values.AddRange([counter, mapping]);
        var reader = f.Reader();
        var snapshot = Assert.Single(await reader.ReadAll(default));
        Assert.Equal(2, snapshot.CheckoutMappings!.Length);
        Assert.True(await reader.IsCurrentAggregate([snapshot], f.H.Zone.ZoneId, default));
        Assert.False(await reader.IsCurrentAggregate([snapshot with { CheckoutMappings = [snapshot.CheckoutMappings[0]] }], f.H.Zone.ZoneId, default));
        Assert.False(await reader.IsCurrentAggregate([snapshot, snapshot], f.H.Zone.ZoneId, default));
    }

    [Fact]
    public async Task OverflowSentinelRejectsCounterCoverageBeforeReadingSources()
    {
        var f = new CheckoutFixture();
        f.Options.MaxCountersPerZone = 1;
        f.Store.Values.Add(new CheckoutCounter { CheckoutCounterId = Guid.NewGuid(), ZoneId = f.H.Zone.ZoneId, IsEnabled = true });
        var error = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() => f.Reader().ReadAll(default));
        Assert.Equal("CHECKOUT_LIMIT_EXCEEDED", error.Code);
        Assert.Equal(1, f.Store.CheckoutReads);
    }

    [Fact]
    public async Task OverflowSentinelRejectsExtraSourceRatherThanSilentlyDroppingIt()
    {
        var f = new CheckoutFixture();
        f.Options.MaxSourcesPerCounter = 1;
        f.AddSource(queueSource: false);
        var error = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() => f.Reader().ReadAll(default));
        Assert.Equal("CHECKOUT_LIMIT_EXCEEDED", error.Code);
    }

    [Theory]
    [InlineData("config")]
    [InlineData("parameters")]
    [InlineData("rule-version")]
    [InlineData("counter-name")]
    [InlineData("counter-version")]
    [InlineData("mapping-version")]
    [InlineData("staff-roi")]
    [InlineData("lane-roi")]
    [InlineData("camera-code")]
    [InlineData("camera-name")]
    [InlineData("camera-floor")]
    [InlineData("camera-status")]
    [InlineData("connection-version")]
    public async Task CheckoutFingerprintIncludesSourceConfigurationAndPolicyVersions(string change)
    {
        var f = new CheckoutFixture();
        var reader = f.Reader();
        var before = Assert.Single(await reader.ReadAll(default));
        switch (change)
        {
            case "config": f.H.Configuration.UpdatedAt = f.H.Configuration.UpdatedAt.AddSeconds(1); break;
            case "parameters": f.Rule.ParametersJson = f.Rule.ParametersJson!.Replace("3", "4"); break;
            case "rule-version": f.Rule.UpdatedAt = f.Rule.UpdatedAt.AddSeconds(1); break;
            case "counter-name": f.Counter.Name = "Renamed lane"; break;
            case "counter-version": f.Counter.UpdatedAt = f.Counter.UpdatedAt.AddSeconds(1); break;
            case "mapping-version": f.Mapping.UpdatedAt = f.Mapping.UpdatedAt.AddSeconds(1); break;
            case "staff-roi": f.Mapping.StaffRoi = CheckoutFixture.OtherPolygon; break;
            case "lane-roi": f.Mapping.LaneRoi = CheckoutFixture.OtherPolygon; break;
            case "camera-code": f.H.Camera.Code = "RENAMED-CAMERA"; break;
            case "camera-name": f.H.Camera.Name = "Renamed camera"; break;
            case "camera-floor": f.H.Camera.FloorId = Guid.NewGuid(); break;
            case "camera-status": f.H.Camera.Status = "INACTIVE"; break;
            case "connection-version": f.H.Connection.UpdatedAt = f.H.Connection.UpdatedAt.AddSeconds(1); break;
        }
        Assert.NotEqual(before.Fingerprint, Assert.Single(await reader.ReadAll(default)).Fingerprint);
        Assert.False(await reader.IsCurrent(before, f.H.Zone.ZoneId, default));
    }

    [Fact]
    public async Task HealthOnlyCameraUpdatesPreserveCheckoutFingerprintsAndCurrentSnapshots()
    {
        var f = new CheckoutFixture();
        f.AddSource(queueSource: false);
        var reader = f.Reader();
        var before = await reader.ReadAll(default);

        // Health probes update telemetry and the shared UpdatedAt, not source configuration.
        foreach (var camera in f.Store.Values.OfType<Camera>())
        {
            camera.HealthStatus = "ONLINE";
            camera.LastSeenAt = f.H.Configuration.UpdatedAt.AddSeconds(30);
            camera.UpdatedAt = camera.UpdatedAt.AddSeconds(30);
        }

        var after = await reader.ReadAll(default);
        Assert.Equal(before.Select(s => s.Fingerprint), after.Select(s => s.Fingerprint));
        foreach (var snapshot in before)
            Assert.True(await reader.IsCurrent(snapshot, f.H.Zone.ZoneId, default));
        Assert.True(await reader.IsCurrentAggregate(before, f.H.Zone.ZoneId, default));
    }

    [Theory]
    [InlineData("zone")]
    [InlineData("floor")]
    [InlineData("camera")]
    [InlineData("test")]
    [InlineData("connection")]
    [InlineData("lane")]
    [InlineData("staff")]
    [InlineData("queue-source")]
    [InlineData("two-queue-sources")]
    public async Task InvalidCheckoutSourcesFailClosed(string reason)
    {
        var f = new CheckoutFixture();
        f.MakeInvalid(reason);
        var snapshots = await f.Reader().ReadAll(default);
        Assert.NotEmpty(snapshots);
        Assert.All(snapshots, s => Assert.NotNull(s.IssueCode));
    }

    [Fact]
    public async Task DisabledCountersAndMappingsAreExcluded()
    {
        var f = new CheckoutFixture();
        f.AddSource(queueSource: false);
        f.Store.Values.OfType<CameraCheckoutMapping>().Single(m => m.MappingId != f.Mapping.MappingId).IsEnabled = false;
        f.Store.Values.Add(new CheckoutCounter { CheckoutCounterId = Guid.NewGuid(), ZoneId = f.H.Zone.ZoneId, IsEnabled = false });
        Assert.Single(Assert.Single(await f.Reader().ReadAll(default)).CheckoutMappings!);
    }

    [Theory]
    [InlineData("counters")]
    [InlineData("sources")]
    [InlineData("camera-mappings")]
    [InlineData("zones")]
    public async Task CheckoutLimitsRejectInsteadOfTruncatingCoverage(string limit)
    {
        var f = new CheckoutFixture();
        switch (limit)
        {
            case "counters": f.Options.MaxCountersPerZone = 0; break;
            case "sources": f.Options.MaxSourcesPerCounter = 0; break;
            case "camera-mappings": f.Options.MaxMappingsPerCamera = 0; break;
            case "zones": f.Options.MaxZones = 0; break;
        }
        await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() => f.Reader().ReadAll(default));
    }
}

internal sealed class CheckoutFixture
{
    internal static readonly string Polygon = JsonSerializer.Serialize(new Point[] { new(0, 0), new(1, 0), new(0, 1) });
    internal static readonly string OtherPolygon = JsonSerializer.Serialize(new Point[] { new(.1m, .1m), new(.9m, .1m), new(.1m, .9m) });
    internal MonitoringSetupTests.Harness H { get; } = new();
    internal CheckoutStore Store { get; }
    internal CheckoutMonitoringOptions Options { get; } = new() { AiContractEnabled = true };
    internal SchemaGate Schema { get; } = new();
    internal BoundedQueries Queries { get; }
    internal CheckoutCounter Counter { get; }
    internal CameraCheckoutMapping Mapping { get; }
    internal CameraZoneMapping GenericMapping { get; }
    internal MonitoringRule Rule { get; }

    internal CheckoutFixture()
    {
        Store = new(H.Store);
        Queries = new(Store);
        H.Zone.ZoneType = "CHECKOUT";
        H.Configuration.Status = "ACTIVE";
        GenericMapping = Store.Values.OfType<CameraZoneMapping>().Single();
        Store.Values.Remove(GenericMapping);
        H.AddRule("CHECKOUT_CAPACITY_ISSUE", "CHECKOUT_CAPACITY", "PEOPLE_PER_STAFFED_COUNTER");
        Rule = Store.Values.OfType<MonitoringRule>().Single();
        Rule.ParametersJson = "{\"minQueue\":3,\"noStaffedAlertLevel\":\"CRITICAL\"}";
        Counter = new() { CheckoutCounterId = Guid.NewGuid(), ZoneId = H.Zone.ZoneId, Code = "LANE-1", Name = "Lane one", IsEnabled = true };
        Mapping = NewMapping(H.Camera.CameraId, true);
        Store.Values.AddRange([Counter, Mapping]);
    }

    internal MonitoringSnapshotReader Reader() => new(Store, Options, Schema, Queries);
    internal MonitoringSetup Setup(IMonitoringSessionOwnership? owner = null) => new(Store, new MonitoringSetupTests.User("ADMIN"),
        ownership: owner, checkoutOptions: Options, checkoutSchema: Schema, checkoutQueries: Queries);
    private CameraCheckoutMapping NewMapping(Guid cameraId, bool queueSource) => new()
    {
        MappingId = Guid.NewGuid(), CameraId = cameraId, CheckoutCounterId = Counter.CheckoutCounterId,
        LaneRoi = Polygon, StaffRoi = Polygon, IsEnabled = true, IsQueueSource = queueSource
    };
    internal Camera AddSource(bool queueSource)
    {
        var camera = new Camera { CameraId = Guid.NewGuid(), FloorId = H.Zone.FloorId, Code = "TEST-SECOND", Status = "ACTIVE" };
        var connection = new CameraConnection { ConnectionId = Guid.NewGuid(), CameraId = camera.CameraId, SourceType = H.Connection.SourceType,
            Protocol = H.Connection.Protocol, StreamUri = H.Connection.StreamUri, IsEnabled = true, LastTestedAt = H.Connection.LastTestedAt, LastTestResult = "SUCCESS" };
        Store.Values.AddRange([camera, connection, NewMapping(camera.CameraId, queueSource)]);
        return camera;
    }
    internal void MakeInvalid(string reason)
    {
        switch (reason)
        {
            case "zone": H.Zone.ZoneType = "OTHER"; break;
            case "floor": H.Camera.FloorId = Guid.NewGuid(); break;
            case "camera": H.Camera.Status = "INACTIVE"; break;
            case "test": H.Connection.LastTestedAt = null; break;
            case "connection": H.Connection.IsEnabled = false; break;
            case "lane": Mapping.LaneRoi = "[]"; break;
            case "staff": Mapping.StaffRoi = "[]"; break;
            case "queue-source": Mapping.IsQueueSource = false; break;
            case "two-queue-sources": AddSource(queueSource: true); break;
        }
    }

    internal sealed class CheckoutStore(MonitoringSetupTests.MemoryStore inner) : ISetupStore
    {
        internal List<Entity> Values => inner.Values;
        internal bool RejectCheckoutReads { get; set; }
        internal int CheckoutReads { get; private set; }
        internal int ConfigurationReads { get; private set; }
        public Task<T?> Find<T>(Guid id, CancellationToken ct = default) where T : Entity, new() => inner.Find<T>(id, ct);
        public Task<List<T>> List<T>(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) where T : Entity, new()
        {
            if (typeof(T) == typeof(MonitoringConfiguration)) ConfigurationReads++;
            if (typeof(T) == typeof(CheckoutCounter) || typeof(T) == typeof(CameraCheckoutMapping))
            {
                CheckoutReads++;
                if (RejectCheckoutReads) throw new InvalidOperationException("Checkout schema must not be read.");
            }
            return inner.List(filter, ct);
        }
        public Task Add<T>(T entity, CancellationToken ct = default) where T : Entity, new() => inner.Add(entity, ct);
        public Task Update<T>(T entity, CancellationToken ct = default) where T : Entity, new() => inner.Update(entity, ct);
        public Task Remove<T>(T entity, CancellationToken ct = default) where T : Entity, new() => inner.Remove(entity, ct);
        public Task<T> Transaction<T>(Func<Task<T>> work, CancellationToken ct = default) => work();
    }

    internal sealed class SchemaGate : ICheckoutSchema
    {
        internal bool IsReady { get; set; } = true;
        internal int Calls { get; private set; }
        public Task<bool> Ready(CancellationToken ct) { Calls++; return Task.FromResult(IsReady); }
    }

    internal sealed class BoundedQueries(CheckoutStore store) : ICheckoutConfigurationQueries
    {
        public async Task<List<CheckoutCounter>> CountersByZone(Guid zoneId, int limit, CancellationToken ct)
            => (await store.List<CheckoutCounter>(c => c.ZoneId == zoneId, ct)).Take(limit + 1).ToList();
        public async Task<List<CameraCheckoutMapping>> MappingsByCamera(Guid cameraId, int limit, CancellationToken ct)
            => (await store.List<CameraCheckoutMapping>(m => m.CameraId == cameraId, ct)).Take(limit + 1).ToList();
        public async Task<List<CameraCheckoutMapping>> MappingsByCounter(Guid counterId, int limit, CancellationToken ct)
            => (await store.List<CameraCheckoutMapping>(m => m.CheckoutCounterId == counterId, ct)).Take(limit + 1).ToList();
    }
}
