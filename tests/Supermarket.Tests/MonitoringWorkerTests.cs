using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.Monitoring;
using Xunit;
namespace Supermarket.Tests;
public sealed class MonitoringWorkerTests
{
    [Fact]
    public async Task ActiveWithoutViewerProcessesEveryFrameAndStopLastZoneReleasesOwner()
    {
        var h=new MonitoringSetupTests.Harness(); h.AddRule("OVERCROWDING_CONGESTION","CROWD_DENSITY","PEOPLE");
        h.Store.Values.OfType<MonitoringRule>().Single().ParametersJson="{\"measurementMode\":\"PEOPLE_COUNT\"}";
        h.Store.Values.OfType<MonitoringRule>().Single().SustainSec=1;
        h.Configuration.Status="ACTIVE"; var clock=new Clock(); var ai=new AiFake();
        using var provider=Services(h.Store,clock,ai,new Queries()).BuildServiceProvider();
        var coordinator=new MonitoringCameraCoordinator(provider.GetRequiredService<IServiceScopeFactory>(),Options.Create(new MonitoringWorkerOptions()),clock);
        var snapshot=Assert.Single(await provider.GetRequiredService<IMonitoringSnapshotReader>().ReadAll(default));
        var z=Assert.Single(snapshot.Zones);
        AiMeasurementBatch Batch(long seq,long ms,int people)=>new(1,ai.Session,ai.Epoch,seq,clock.UtcNow,ms,snapshot.Fingerprint,[new(z.ZoneId,z.ConfigId,z.ConfigVersion,people,0)]);
        ai.Batches=[Batch(1,0,3),Batch(2,1000,0),Batch(3,2000,3)];
        await coordinator.Tick(default);
        Assert.Empty(h.Store.Values.OfType<Incident>()); Assert.Equal(1,ai.Starts);
        // Low observation between highs must not be skipped by a latest-only poll.
        ai.Batches.Add(Batch(4,3000,3));
        await coordinator.Tick(default);
        Assert.Single(h.Store.Values.OfType<Incident>());
        Assert.Equal("RUNNING",coordinator.Get(h.Camera.CameraId)!.State);
        h.Configuration.Status="INACTIVE";
        await coordinator.Tick(default); Assert.Equal(1,ai.Stops); Assert.False(coordinator.IsMonitoringOwned(h.Camera.CameraId));
    }
    [Fact]
    public async Task CompletedDrainsOnceAndTransportFailureDoesNotAdvanceCursor()
    {
        var h=new MonitoringSetupTests.Harness(); h.AddRule(); h.Configuration.Status="ACTIVE";
        var clock=new Clock(); var ai=new AiFake(); using var provider=Services(h.Store,clock,ai,new Queries()).BuildServiceProvider();
        var coordinator=new MonitoringCameraCoordinator(provider.GetRequiredService<IServiceScopeFactory>(),Options.Create(new MonitoringWorkerOptions()),clock);
        var snapshot=Assert.Single(await provider.GetRequiredService<IMonitoringSnapshotReader>().ReadAll(default)); var z=snapshot.Zones[0];
        ai.Batches=[new(1,ai.Session,ai.Epoch,1,clock.UtcNow,0,snapshot.Fingerprint,[new(z.ZoneId,z.ConfigId,z.ConfigVersion,2,0)])];
        ai.FailRead=true; await coordinator.Tick(default); Assert.Empty(h.Store.Values.OfType<OperationalEvent>());
        clock.UtcNow=clock.UtcNow.AddSeconds(2); ai.FailRead=false; ai.State="COMPLETED";
        await coordinator.Tick(default); await coordinator.Tick(default);
        Assert.Single(h.Store.Values.OfType<OperationalEvent>()); Assert.Equal("COMPLETED",coordinator.Get(h.Camera.CameraId)!.State);
        Assert.Equal(1,ai.Starts); await coordinator.Shutdown(default);
    }
    private static ServiceCollection Services(ISetupStore store,IClock clock,IAiMonitoringClient ai,IMonitoringIncidentQueries queries)
    {
        var services=new ServiceCollection(); services.AddSingleton(store); services.AddSingleton(clock); services.AddSingleton(ai); services.AddSingleton(queries);
        services.AddScoped<IMonitoringSnapshotReader,MonitoringSnapshotReader>(); services.AddScoped<MonitoringIncidentWriter>(); return services;
    }
    [Fact]
    public async Task ChangedSourceReleasesOldOwnerBeforeStartingNewSource()
    {
        var h=new MonitoringSetupTests.Harness(); h.AddRule(); h.Configuration.Status="ACTIVE";
        var clock=new Clock(); var ai=new AiFake { RejectSourceChange=true };
        using var provider=Services(h.Store,clock,ai,new Queries()).BuildServiceProvider();
        var coordinator=new MonitoringCameraCoordinator(provider.GetRequiredService<IServiceScopeFactory>(),Options.Create(new MonitoringWorkerOptions()),clock);
        await coordinator.Tick(default);
        h.Store.Values.OfType<CameraConnection>().Single().StreamUri="http://changed.test/video";
        h.Store.Values.OfType<CameraConnection>().Single().UpdatedAt=clock.UtcNow.AddSeconds(1);
        await coordinator.Tick(default);
        Assert.Equal(1,ai.Stops); Assert.Equal(2,ai.Starts);
        Assert.NotEqual("ERROR",coordinator.Get(h.Camera.CameraId)!.State);
        await coordinator.Shutdown(default);
    }
    private sealed class Clock:IClock { public DateTime UtcNow {get;set;}=new(2026,10,3,0,0,0,DateTimeKind.Utc); }
    [Fact]
    public async Task HighFrameRateWritesOnlySamplesOrSeverityTransitionsAndRetainsOpenIncident()
    {
        var h=new MonitoringSetupTests.Harness(); h.AddRule("OVERCROWDING_CONGESTION","CROWD_DENSITY","PEOPLE");
        var r=h.Store.Values.OfType<MonitoringRule>().Single(); r.ParametersJson="{\"measurementMode\":\"PEOPLE_COUNT\"}"; r.SustainSec=0; r.WarningThreshold=1; r.CriticalThreshold=4; h.Configuration.Status="ACTIVE";
        var counted=new CountingStore(h.Store); var clock=new Clock(); var ai=new AiFake();
        using var provider=Services(counted,clock,ai,new Queries()).BuildServiceProvider();
        var coordinator=new MonitoringCameraCoordinator(provider.GetRequiredService<IServiceScopeFactory>(),Options.Create(new MonitoringWorkerOptions()),clock);
        var snap=Assert.Single(await provider.GetRequiredService<IMonitoringSnapshotReader>().ReadAll(default)); var z=snap.Zones[0];
        AiMeasurementBatch Batch(long seq,long ms,int people)=>new(1,ai.Session,ai.Epoch,seq,clock.UtcNow,ms,snap.Fingerprint,[new(z.ZoneId,z.ConfigId,z.ConfigVersion,people,0)]);
        ai.Batches=[Batch(1,0,3),Batch(2,16,3),Batch(3,33,3)]; await coordinator.Tick(default);
        Assert.Equal(1,counted.Transactions); var incident=Assert.Single(h.Store.Values.OfType<Incident>());
        ai.Batches.Add(Batch(4,50,0)); await coordinator.Tick(default);
        Assert.Equal(incident.IncidentId,coordinator.Get(h.Camera.CameraId)!.Zones[0].Rules[0].IncidentId);
        ai.Batches.Add(Batch(5,66,5)); await coordinator.Tick(default);
        Assert.Equal(2,counted.Transactions); Assert.Equal("CRITICAL",incident.Severity); await coordinator.Shutdown(default);
    }
    private sealed class CountingStore(ISetupStore inner):ISetupStore {
        public int Transactions;
        public Task<T?> Find<T>(Guid id,CancellationToken ct=default) where T:Entity,new()=>inner.Find<T>(id,ct);
        public Task<List<T>> List<T>(System.Linq.Expressions.Expression<Func<T,bool>>? filter=null,CancellationToken ct=default) where T:Entity,new()=>inner.List(filter,ct);
        public Task Add<T>(T e,CancellationToken ct=default) where T:Entity,new()=>inner.Add(e,ct);
        public Task Update<T>(T e,CancellationToken ct=default) where T:Entity,new()=>inner.Update(e,ct);
        public Task Remove<T>(T e,CancellationToken ct=default) where T:Entity,new()=>inner.Remove(e,ct);
        public Task<T> Transaction<T>(Func<Task<T>> work,CancellationToken ct=default) { Transactions++; return inner.Transaction(work,ct); }
    }
    [Fact]
    public async Task PartialFrameRetryKeepsSuccessfulRuleAndDoesNotDuplicateEvidence()
    {
        var h=new MonitoringSetupTests.Harness(); h.AddRule(); h.AddRule("OVERCROWDING_CONGESTION","CROWD_DENSITY","PEOPLE");
        var rules=h.Store.Values.OfType<MonitoringRule>().ToArray();
        foreach(var rule in rules) { rule.SustainSec=1; rule.WarningThreshold=1; rule.CriticalThreshold=4; }
        rules[1].ParametersJson="{\"measurementMode\":\"PEOPLE_COUNT\"}"; h.Configuration.Status="ACTIVE";
        var clock=new Clock(); var ai=new AiFake(); var queries=new Queries { FailSecondOnce=true };
        using var provider=Services(h.Store,clock,ai,queries).BuildServiceProvider();
        var coordinator=new MonitoringCameraCoordinator(provider.GetRequiredService<IServiceScopeFactory>(),Options.Create(new MonitoringWorkerOptions()),clock);
        var snapshot=Assert.Single(await provider.GetRequiredService<IMonitoringSnapshotReader>().ReadAll(default)); var z=snapshot.Zones[0];
        AiMeasurementBatch Batch(long seq,long ms)=>new(1,ai.Session,ai.Epoch,seq,clock.UtcNow,ms,snapshot.Fingerprint,[new(z.ZoneId,z.ConfigId,z.ConfigVersion,3,3)]);
        ai.Batches=[Batch(1,0)]; await coordinator.Tick(default);
        Assert.Single(h.Store.Values.OfType<OperationalEvent>()); Assert.Equal("ERROR",coordinator.Get(h.Camera.CameraId)!.State);
        clock.UtcNow=clock.UtcNow.AddSeconds(2); ai.Batches.Add(Batch(2,1000));
        await coordinator.Tick(default);
        Assert.Equal(2,h.Store.Values.OfType<Incident>().Count()); Assert.Equal(4,h.Store.Values.OfType<OperationalEvent>().Count());
        await coordinator.Tick(default); Assert.Equal(4,h.Store.Values.OfType<OperationalEvent>().Count()); await coordinator.Shutdown(default);
    }
    private sealed class AiFake:IAiMonitoringClient
    {
        public Guid Session=Guid.NewGuid(),Epoch=Guid.NewGuid(); public int Starts,Stops; public bool FailRead; public string State="LIVE"; public List<AiMeasurementBatch> Batches=[];
        public bool RejectSourceChange; private string? priorUri;
        public Task<AiPreviewStatusView> Start(MonitoringCameraSnapshot s,Guid owner,CancellationToken ct) {
            if(RejectSourceChange && priorUri is not null && priorUri!=s.Connection.StreamUri) throw new Supermarket.Application.ApplicationException("AI_SESSION_MONITORING_OWNED","Source changed",409);
            priorUri=s.Connection.StreamUri; Starts++; return Task.FromResult(new AiPreviewStatusView(s.CameraId,State,null,DateTime.UtcNow,0,null,Session,"MONITORING",s.Fingerprint)); }
        public Task<AiMeasurementPage> Read(Guid camera,Guid owner,Guid? session,long after,int limit,CancellationToken ct) {
            if(FailRead) throw new Supermarket.Application.ApplicationException("AI_SERVICE_UNAVAILABLE","Unavailable",503);
            return Task.FromResult(new AiMeasurementPage(1,Session,Batches.FirstOrDefault()?.Sequence??1,Batches.LastOrDefault()?.Sequence??0,false,Batches.Where(b=>b.Sequence>after).Take(limit).ToArray(),State,null));
        }
        public Task Stop(Guid camera,Guid owner,CancellationToken ct) { Stops++; priorUri=null; return Task.CompletedTask; }
    }
    private sealed class Queries:IMonitoringIncidentQueries {
        public bool FailSecondOnce; private int calls;
        public Task EnsureSchema(CancellationToken ct)=>Task.CompletedTask;
        public Task<DateTime?> LatestEndedAt(Guid z,Guid t,CancellationToken ct) {
            if(FailSecondOnce && ++calls==2) throw new Supermarket.Application.ApplicationException("CONCURRENT_UPDATE","Controlled retry",409);
            return Task.FromResult<DateTime?>(null);
        }
        public Task<IncidentFeedView> PageForCamera(Guid c,IncidentFeedQuery q,CancellationToken ct)=>throw new NotSupportedException();
    }
}
