using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
namespace Supermarket.Tests;

public sealed class MonitoringIncidentWriterTests
{
    [Fact]
    public async Task OpensUpdatesEscalatesClearsAndReopensOneEpisodePerKey()
    {
        var store=new MonitoringSetupTests.MemoryStore(); var reader=new Current(); var queries=new Queries(); var clock=new Clock();
        var writer=new MonitoringIncidentWriter(store,reader,queries,clock);
        var eventTypeId=Guid.NewGuid();
        var rule=new MonitoringRuleSnapshot(Guid.NewGuid(),Guid.NewGuid(),"OVERCROWDING_CONGESTION","Crowding","PEOPLE_COUNT","PEOPLE",1,2,1,10,eventTypeId,"CONGESTION",1);
        var zone=new MonitoringZoneSnapshot(Guid.NewGuid(),"ROI",Guid.NewGuid(),clock.UtcNow,[],.5m,[rule]);
        var snapshot=new MonitoringCameraSnapshot(Guid.NewGuid(),new(),"f",[zone]);
        var session=Guid.NewGuid(); var continuity=Guid.NewGuid();
        IncidentObservationCommand Command(long seq,int count,string? severity,bool clear=false)=>new(snapshot,zone,rule,
            new(1,session,continuity,seq,clock.UtcNow.AddSeconds(seq),seq*1000,"f",[new(zone.ZoneId,zone.ConfigId,zone.ConfigVersion,count,0)]),
            new(zone.ZoneId,zone.ConfigId,zone.ConfigVersion,count,0),new(new(),severity,1000,0,clear?"RECOVERY_SUSTAINED":"THRESHOLD_SUSTAINED",true,clear),true);

        var first=Command(1,1,"WARNING");
        var opened=await writer.Apply(first,default);
        var episode=Assert.Single(store.Values.OfType<OperationalEvent>());
        Assert.Equal(opened.OperationalEventId,episode.EventId); Assert.Equal("ACTIVE",episode.Status); Assert.Equal("WARNING",episode.Priority);
        Assert.Equal(eventTypeId,episode.EventTypeId); Assert.Null(episode.ClearedAt);
        Assert.True((await writer.Apply(first,default)).Idempotent); Assert.Single(store.Values.OfType<OperationalEvent>());

        await writer.Apply(Command(2,3,"CRITICAL"),default);
        episode=Assert.Single(store.Values.OfType<OperationalEvent>()); Assert.Equal("CRITICAL",episode.Priority); Assert.Equal(3,episode.MetricValue);
        await writer.Apply(Command(3,0,null),default);
        episode=Assert.Single(store.Values.OfType<OperationalEvent>()); Assert.Null(episode.ClearedAt); Assert.Equal(0,episode.MetricValue);
        await writer.Apply(Command(4,0,null,true),default);
        episode=Assert.Single(store.Values.OfType<OperationalEvent>()); Assert.Equal("CLEARED",episode.Status); Assert.NotNull(episode.ClearedAt);

        await writer.Apply(Command(5,2,"WARNING"),default);
        Assert.Equal(2,store.Values.OfType<OperationalEvent>().Count()); Assert.Single(store.Values.OfType<OperationalEvent>(),e=>e.ClearedAt is null);
        Assert.Empty(store.Values.OfType<Incident>());
        reader.Valid=false;
        Assert.True((await writer.Apply(Command(6,3,"CRITICAL"),default)).Stale); Assert.Equal(2,store.Values.OfType<OperationalEvent>().Count());
    }

    [Fact]
    public async Task OperationalEventOutcomeNeverCreatesIncident()
    {
        var store=new MonitoringSetupTests.MemoryStore(); var clock=new Clock(); var queries=new Queries { Ended=clock.UtcNow.AddSeconds(-5) };
        var writer=new MonitoringIncidentWriter(store,new Current(),queries,clock); var eventTypeId=Guid.NewGuid();
        var zone=new MonitoringZoneSnapshot(Guid.NewGuid(),"ROI",Guid.NewGuid(),clock.UtcNow,[],.5m,[]);
        var rule=new MonitoringRuleSnapshot(Guid.NewGuid(),Guid.NewGuid(),"LONG_QUEUE","Queue","QUEUE_LENGTH","PEOPLE",1,2,1,10,eventTypeId,"LONG_QUEUE",1);
        var snapshot=new MonitoringCameraSnapshot(Guid.NewGuid(),new(),"f",[zone]);
        var measurement=new AiZoneMeasurement(zone.ZoneId,zone.ConfigId,zone.ConfigVersion,3,2);
        var command=new IncidentObservationCommand(snapshot,zone,rule,new(1,Guid.NewGuid(),Guid.NewGuid(),1,clock.UtcNow,1000,"f",[measurement]),measurement,new(new(),"WARNING",1000,0,"THRESHOLD_SUSTAINED"),true);
        var result=await writer.Apply(command,default);
        Assert.Equal("THRESHOLD_SUSTAINED",result.Reason); Assert.Equal(0,result.CooldownRemainingSec); Assert.Empty(store.Values.OfType<Incident>());
        var episode=Assert.Single(store.Values.OfType<OperationalEvent>()); Assert.Equal("ACTIVE",episode.Status);
    }

    [Fact]
    public async Task IncidentOutcomeCreatesIncidentWithoutOperationalEvent()
    {
        var store=new MonitoringSetupTests.MemoryStore(); var clock=new Clock(); var writer=new MonitoringIncidentWriter(store,new Current(),new Queries(),clock);
        var ruleId=Guid.NewGuid(); var incidentTypeId=Guid.NewGuid();
        var rule=new MonitoringRuleSnapshot(ruleId,incidentTypeId,"SAFETY_HAZARD","Safety hazard","HAZARD","BOOLEAN",0,1,0,10,
            Guid.Empty,"",null,"INCIDENT");
        var zone=new MonitoringZoneSnapshot(Guid.NewGuid(),"ROI",Guid.NewGuid(),clock.UtcNow,[],.5m,[rule]);
        var snapshot=new MonitoringCameraSnapshot(Guid.NewGuid(),new(),"f",[zone]);
        var measurement=new AiZoneMeasurement(zone.ZoneId,zone.ConfigId,zone.ConfigVersion,1,0);
        var result=await writer.Apply(new(snapshot,zone,rule,new(1,Guid.NewGuid(),Guid.NewGuid(),1,clock.UtcNow,0,"f",[measurement]),measurement,
            new(new(),"CRITICAL",0,0,"THRESHOLD_SUSTAINED"),true),default);

        var incident=Assert.Single(store.Values.OfType<Incident>());
        Assert.Equal(result.IncidentId,incident.IncidentId);
        Assert.Equal(ruleId,incident.MonitoringRuleId);
        Assert.Equal(incidentTypeId,incident.IncidentTypeId);
        Assert.Empty(store.Values.OfType<OperationalEvent>());
    }

    [Fact]
    public async Task SameCameraAndTypeInTwoZonesCreateIndependentEpisodes()
    {
        var store=new MonitoringSetupTests.MemoryStore(); var clock=new Clock(); var writer=new MonitoringIncidentWriter(store,new Current(),new Queries(),clock);
        var cameraId=Guid.NewGuid(); var eventTypeId=Guid.NewGuid(); var incidentTypeId=Guid.NewGuid();
        var rule=new MonitoringRuleSnapshot(Guid.NewGuid(),incidentTypeId,"OVERCROWDING_CONGESTION","Crowding","PEOPLE_COUNT","PEOPLE",1,2,0,10,eventTypeId,"CONGESTION",1);
        var zones=new[] { Zone("A"),Zone("B") }; var snapshot=new MonitoringCameraSnapshot(cameraId,new(),"f",zones);
        foreach(var zone in zones)
        {
            var measurement=new AiZoneMeasurement(zone.ZoneId,zone.ConfigId,zone.ConfigVersion,3,0);
            await writer.Apply(new(snapshot,zone,rule,new(1,Guid.NewGuid(),Guid.NewGuid(),1,clock.UtcNow,0,"f",[measurement]),measurement,
                new(new(),"CRITICAL",0,0,"THRESHOLD_SUSTAINED"),true),default);
        }
        var episodes=store.Values.OfType<OperationalEvent>().ToArray(); Assert.Equal(2,episodes.Length); Assert.Equal(2,episodes.Select(e=>e.ZoneId).Distinct().Count());
        Assert.All(episodes,e=>Assert.Equal(cameraId,e.CameraId)); Assert.All(episodes,e=>Assert.Equal(eventTypeId,e.EventTypeId));
        MonitoringZoneSnapshot Zone(string name)=>new(Guid.NewGuid(),name,Guid.NewGuid(),clock.UtcNow,[],.5m,[rule]);
    }

    private sealed class Clock:IClock { public DateTime UtcNow=>new(2026,10,3,0,0,0,DateTimeKind.Utc); }
    private sealed class Current:IMonitoringSnapshotReader
    { public bool Valid=true; public Task<MonitoringCameraSnapshot[]> ReadAll(CancellationToken ct)=>Task.FromResult(Array.Empty<MonitoringCameraSnapshot>()); public Task<bool> IsCurrent(MonitoringCameraSnapshot s,Guid z,CancellationToken ct)=>Task.FromResult(Valid); }
    private sealed class Queries:IMonitoringIncidentQueries
    {
        public DateTime? Ended;
        public Task EnsureSchema(CancellationToken ct)=>Task.CompletedTask;
        public Task<DateTime?> LatestEndedAt(Guid zone,Guid type,CancellationToken ct)=>Task.FromResult(Ended);
        public Task<IncidentFeedView> PageForCamera(Guid camera,IncidentFeedQuery q,CancellationToken ct)=>throw new NotSupportedException();
    }
}
