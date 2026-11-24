using System.Net;
using System.Net.Http.Json;
using Supermarket.Application;
using Supermarket.Domain;
using Xunit;

namespace Supermarket.Tests;

public sealed class CheckoutSourceGuardTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);
    [Fact]
    public async Task CheckoutOnlyMonitoringPreventsSourceReplacementBeforeAnyMutation()
    {
        var w = new World();
        var cameras = new CameraSetup(w.Store, new MonitoringSetupTests.User("ADMIN"), new Stream(), new Protector(), new Clock(), w.Guard);
        var error = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() => cameras.Configure(w.Camera.CameraId,
            new("LIVE", "HTTP", "http://changed.test/video"), default));
        Assert.Equal("MONITORING_ACTIVE", error.Code);
        Assert.Equal("http://camera.test/video", w.Connection.StreamUri);
        Assert.True(w.Connection.IsEnabled);
    }

    [Fact]
    public async Task ZoneCannotChangeTypeWhileCountersExistEvenWhenCounterIsDisabled()
    {
        var w = new World(); w.Counter.IsEnabled = false;
        var setup = new StoreSetup(w.Store, new MonitoringSetupTests.User("ADMIN"), w.Guard);
        var error = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() => setup.SaveZone(w.Zone.ZoneId, null,
            new("new-code", "New name", "AISLE", [new(0,0), new(1,0), new(0,1)]), default));
        Assert.Equal("CHECKOUT_ZONE_REQUIRED", error.Code);
        Assert.Equal("CHECKOUT", w.Zone.ZoneType); Assert.Equal("original", w.Zone.Code);
    }

    [Fact]
    public async Task OfflineBlockedAndUnknownProcessingAreNotNegativePersonDetections()
    {
        var w = new World(); var health = new CameraHealthRuntimeState();
        var ready = new CheckoutCameraUsability(w.Store, health);
        Assert.False(await ready.Ready(w.Camera.CameraId, default));
        health.Observe(w.Camera, new(true, "FRAME_RECEIVED", true, []), [], Now);
        Assert.True(await ready.Ready(w.Camera.CameraId, default));
        w.Store.Values.Add(new CameraHealthEvent { HealthEventId=Guid.NewGuid(), CameraId=w.Camera.CameraId,
            EventType=CameraHealthEventTypes.ViewBlocked, Status="OPEN" });
        Assert.False(await ready.Ready(w.Camera.CameraId, default));
        Assert.Equal("ACTIVE", Assert.Single(w.Store.Values.OfType<MonitoringConfiguration>()).Status);
    }

    [Fact]
    public async Task CameraRuntimeShowsCheckoutOnlyMonitoringAndItsMeasurements()
    {
        var w = new World();
        var configuration = Assert.Single(w.Store.Values.OfType<MonitoringConfiguration>());
        var type = new OperationalEventType { EventTypeId = Guid.NewGuid(), Code = "LONG_QUEUE", Name = "Long Queue", MeasurementType = "QUEUE_LENGTH" };
        var rule = new MonitoringRule { RuleId = Guid.NewGuid(), ConfigId = configuration.ConfigId, EventTypeId = type.EventTypeId,
            ThresholdUnit = "PEOPLE", WarningThreshold = 3, CriticalThreshold = 5, SustainSec = 2, ParametersJson = RuleHysteresis.Merge(null, 2, 4) };
        w.Store.Values.AddRange([type, rule]);
        Assert.Empty(w.Store.Values.OfType<CameraZoneMapping>());
        var state = new LiveState();
        var live = new MonitoringLive(w.Store, new MonitoringSetupTests.User("OPERATOR"), state, state, state, w.Guard, state, state);

        var starting = await live.Runtime(w.Camera.CameraId, default);
        Assert.Equal("STARTING", starting.State);
        Assert.True(starting.HasCheckoutMonitoring);
        var pending = Assert.Single(Assert.Single(starting.Zones).Rules);
        Assert.Equal("CHECKOUT_QUEUE_LENGTH", pending.Mode);
        Assert.Null(pending.MetricValue);

        state.Camera = new(w.Camera.CameraId, "RUNNING", "MEASUREMENTS_AVAILABLE", null, "RECORDED", Guid.NewGuid(), 30_000,
            Now, false, null, [], HasCheckoutMonitoring: true);
        state.Checkout = new(w.Zone.ZoneId, configuration.ConfigId, configuration.UpdatedAt, Now, "READY", null, 5, 1, 5,
            false, [], [pending with { MetricValue = 5, CriticalProgressMs = 1500 }], LanePeopleCount: 7);
        var running = await live.Runtime(w.Camera.CameraId, default);
        Assert.Equal("RUNNING", running.State);
        var checkout = Assert.Single(running.Zones);
        Assert.Equal(5, checkout.QueueCount);
        Assert.Equal(7L, checkout.LanePeopleCount);
        Assert.Equal(1500, Assert.Single(checkout.Rules).CriticalProgressMs);

        state.Camera = state.Camera with { State = "COMPLETED" };
        Assert.Equal("COMPLETED", (await live.Runtime(w.Camera.CameraId, default)).State);
        configuration.Status = "INACTIVE";
        Assert.Equal("STOPPING", (await live.Runtime(w.Camera.CameraId, default)).State);
        state.Camera = null;
        Assert.Equal("STOPPED", (await live.Runtime(w.Camera.CameraId, default)).State);
    }

    [Theory, InlineData(null,401), InlineData("STAFF",403), InlineData("ADMIN",200), InlineData("OPERATOR",200), InlineData("MANAGER",200)]
    public async Task RuntimeReadIsAuthorizedAndNeverReturnsZeroWhenNotActive(string? role, int expected)
    {
        var w = new World(); await using var factory = new SetupOverviewTests.Factory(w.Store);
        using var client = factory.CreateClient(); if(role is not null) client.DefaultRequestHeaders.Add("X-Test-Role",role);
        var response = await client.GetAsync($"/api/zones/{w.Zone.ZoneId}/checkout-runtime");
        Assert.Equal((HttpStatusCode)expected,response.StatusCode);
        if(expected==200)
        {
            var view=await response.Content.ReadFromJsonAsync<CheckoutZoneRuntimeView>();
            Assert.Equal("NOT_ACTIVE",view!.State); Assert.Null(view.QueueCount); Assert.Null(view.StaffedCounterCount);
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
    }

    private sealed class World
    {
        public MonitoringSetupTests.MemoryStore Store {get;}=new();
        public Zone Zone {get;}=new(){ZoneId=Guid.NewGuid(),FloorId=Guid.NewGuid(),ZoneType="CHECKOUT",Code="original"};
        public Camera Camera {get;}
        public CameraConnection Connection {get;}
        public CheckoutCounter Counter {get;}
        public CheckoutSourceGuard Guard {get;}
        public World()
        {
            Camera=new(){CameraId=Guid.NewGuid(),FloorId=Zone.FloorId,Status="ACTIVE",HealthStatus="ONLINE"};
            Connection=new(){ConnectionId=Guid.NewGuid(),CameraId=Camera.CameraId,SourceType="LIVE",Protocol="HTTP",StreamUri="http://camera.test/video",
                IsEnabled=true,LastTestResult="SUCCESS",LastTestedAt=Now};
            Counter=new(){CheckoutCounterId=Guid.NewGuid(),ZoneId=Zone.ZoneId,IsEnabled=true};
            Store.Values.AddRange([Zone,Camera,Connection,Counter,new Floor{FloorId=Zone.FloorId},
                new CameraCheckoutMapping{MappingId=Guid.NewGuid(),CameraId=Camera.CameraId,CheckoutCounterId=Counter.CheckoutCounterId,IsEnabled=true,IsQueueSource=true},
                new MonitoringConfiguration{ConfigId=Guid.NewGuid(),ZoneId=Zone.ZoneId,Status="ACTIVE"}]);
            Guard=new(Store,new Schema(),new Queries(Store),new());
        }
    }
    private sealed class Schema:ICheckoutSchema {public Task<bool> Ready(CancellationToken ct)=>Task.FromResult(true);}
    private sealed class LiveState : IMonitoringRuntimeState, ICheckoutRuntimeState, IMonitoringIncidentQueries, IMonitoringSnapshotReader, IRecordedMonitoringReplay
    {
        public MonitoringCameraRuntimeView? Camera { get; set; }
        public CheckoutZoneRuntimeView? Checkout { get; set; }
        MonitoringCameraRuntimeView? IMonitoringRuntimeState.Get(Guid cameraId) => Camera;
        CheckoutZoneRuntimeView? ICheckoutRuntimeState.Get(Guid zoneId) => Checkout;
        public Task EnsureSchema(CancellationToken ct) => Task.CompletedTask;
        public Task<IncidentFeedView> PageForCamera(Guid id, IncidentFeedQuery query, CancellationToken ct) => throw new NotSupportedException();
        public Task<MonitoringCameraSnapshot[]> ReadAll(CancellationToken ct) => Task.FromResult(Array.Empty<MonitoringCameraSnapshot>());
        public Task<bool> IsCurrent(MonitoringCameraSnapshot snapshot, Guid zoneId, CancellationToken ct) => throw new NotSupportedException();
        public Task Replay(Guid cameraId, Guid completedSessionId, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Queries(MonitoringSetupTests.MemoryStore store):ICheckoutConfigurationQueries
    {
        public Task<List<CheckoutCounter>> CountersByZone(Guid id,int limit,CancellationToken ct)=>Task.FromResult(store.Values.OfType<CheckoutCounter>().Where(c=>c.ZoneId==id).Take(limit+1).ToList());
        public Task<List<CameraCheckoutMapping>> MappingsByCamera(Guid id,int limit,CancellationToken ct)=>Task.FromResult(store.Values.OfType<CameraCheckoutMapping>().Where(m=>m.CameraId==id).Take(limit+1).ToList());
        public Task<List<CameraCheckoutMapping>> MappingsByCounter(Guid id,int limit,CancellationToken ct)=>Task.FromResult(store.Values.OfType<CameraCheckoutMapping>().Where(m=>m.CheckoutCounterId==id).Take(limit+1).ToList());
    }
    private sealed class Stream:ICameraStream
    {
        public Task<ProbeResult> Test(CameraConnection c,CancellationToken ct)=>throw new NotSupportedException();
        public Task<PreviewFrame> Preview(CameraConnection c,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class Protector:ICredentialProtector {public string Protect(string s)=>s;public string Unprotect(string s)=>s;}
    private sealed class Clock:IClock {public DateTime UtcNow=>Now;}
}
