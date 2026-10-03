using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
namespace Supermarket.Tests;
public sealed class MonitoringIncidentWriterTests
{
    [Fact]
    public async Task CreatesDetectedDeduplicatesEscalatesAndNeverClosesOnCountDrop()
    {
        var store=new MonitoringSetupTests.MemoryStore(); var reader=new Current(); var queries=new Queries();
        var writer=new MonitoringIncidentWriter(store,reader,queries,new Clock());
        var snapshot=new MonitoringCameraSnapshot(Guid.NewGuid(),new(),"f",[]);
        var rule=new MonitoringRuleSnapshot(Guid.NewGuid(),Guid.NewGuid(),"OVERCROWDING_CONGESTION","Crowding","PEOPLE_COUNT","PEOPLE",1,2,1,10);
        var zone=new MonitoringZoneSnapshot(Guid.NewGuid(),"ROI",Guid.NewGuid(),DateTime.UtcNow,[],.5m,[rule]);
        var session=Guid.NewGuid(); var epoch=Guid.NewGuid();
        IncidentObservationCommand Command(long seq,int count,string? severity,bool sample=true)=>new(snapshot,zone,rule,
            new(1,session,epoch,seq,DateTime.UtcNow,seq*1000,"f",[new(zone.ZoneId,zone.ConfigId,zone.ConfigVersion,count,0)]),
            new(zone.ZoneId,zone.ConfigId,zone.ConfigVersion,count,0),new(new(),severity,1000,0,"THRESHOLD_SUSTAINED"),sample);
        var command=Command(1,1,"WARNING");
        var created=await writer.Apply(command,default);
        var incident=Assert.Single(store.Values.OfType<Incident>());
        Assert.Equal("DETECTED",incident.Status); Assert.Equal("AI_DETECTED",incident.SourceType); Assert.Null(incident.ReportedByUserId);
        Assert.Equal(created.IncidentId,incident.IncidentId);
        Assert.True((await writer.Apply(command,default)).Idempotent);
        Assert.Single(store.Values.OfType<OperationalEvent>());
        await writer.Apply(Command(2,3,"CRITICAL"),default);
        await writer.Apply(Command(3,0,null),default);
        incident=Assert.Single(store.Values.OfType<Incident>());
        Assert.Equal("CRITICAL",incident.Severity); Assert.Null(incident.ClosedAt); Assert.Equal("DETECTED",incident.Status);
        Assert.Equal(3,store.Values.OfType<OperationalEvent>().Count());
        Assert.All(store.Values.OfType<OperationalEvent>(),e=>Assert.Equal(incident.IncidentId,e.IncidentId));
        var metadata=store.Values.OfType<OperationalEvent>().First().MetadataJson!;
        Assert.Contains("PEOPLE_COUNT",metadata); Assert.DoesNotContain("trackId",metadata); Assert.DoesNotContain("streamUri",metadata);
        reader.Valid=false;
        Assert.True((await writer.Apply(Command(4,3,"CRITICAL"),default)).Stale);
        Assert.Equal(3,store.Values.OfType<OperationalEvent>().Count());
    }
    [Fact]
    public async Task UnsustainedObservationAndCooldownStayDistinctFromIncidents()
    {
        var store=new MonitoringSetupTests.MemoryStore(); var clock=new Clock(); var queries=new Queries { Ended=clock.UtcNow.AddSeconds(-5) };
        var writer=new MonitoringIncidentWriter(store,new Current(),queries,clock);
        var zone=new MonitoringZoneSnapshot(Guid.NewGuid(),"ROI",Guid.NewGuid(),clock.UtcNow,[],.5m,[]);
        var rule=new MonitoringRuleSnapshot(Guid.NewGuid(),Guid.NewGuid(),"LONG_QUEUE","Queue","QUEUE_LENGTH","PEOPLE",1,2,1,10);
        var snapshot=new MonitoringCameraSnapshot(Guid.NewGuid(),new(),"f",[zone]);
        var measurement=new AiZoneMeasurement(zone.ZoneId,zone.ConfigId,zone.ConfigVersion,3,2);
        var command=new IncidentObservationCommand(snapshot,zone,rule,new(1,Guid.NewGuid(),Guid.NewGuid(),1,clock.UtcNow,1000,"f",[measurement]),measurement,new(new(),"WARNING",1000,0,"THRESHOLD_SUSTAINED"),true);
        var result=await writer.Apply(command,default);
        Assert.Equal("COOLDOWN",result.Reason); Assert.Equal(5,result.CooldownRemainingSec);
        Assert.Empty(store.Values.OfType<Incident>()); Assert.Equal("SUPPRESSED",Assert.Single(store.Values.OfType<OperationalEvent>()).Status);
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
