using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
namespace Supermarket.Tests;
[Collection("SqlApi")]
[Trait("Category", "SqlIntegration")]
public sealed class MonitoringRuntimeApiFlowTests
{
    [Fact]
    public async Task TwoZoneVersionsReconfigureWithoutStopOrRecordedRewind()
    {
        await using var h=new MonitoringRuntimeTestSupport(); await h.Initialize();
        var (camera,zone,first)=await h.Configure();
        var secondZone=await MonitoringRuntimeTestSupport.Read<ZoneView>(await h.Client.PostAsJsonAsync($"/api/floors/{camera.FloorId}/zones",new ZoneRequest(Guid.NewGuid().ToString("N"),"Second ROI","QUEUE",MonitoringRuntimeTestSupport.Roi)));
        await MonitoringRuntimeTestSupport.Read<MappingView>(await h.Client.PutAsJsonAsync($"/api/cameras/{camera.CameraId}/zones/{secondZone.ZoneId}",new MappingRequest(MonitoringRuntimeTestSupport.Roi)));
        var queue=(await h.Client.GetFromJsonAsync<IncidentTypeView[]>("/api/incident-types"))!.Single(t=>t.Code=="LONG_QUEUE");
        var second=await MonitoringRuntimeTestSupport.Read<MonitoringConfigurationView>(await h.Client.PutAsJsonAsync($"/api/zones/{secondZone.ZoneId}/monitoring",new MonitoringRequest("Second zone",.8m,[new(queue.IncidentTypeId,1,2,"PEOPLE",1,30)])));
        first=await h.Activate(zone.ZoneId,first); await h.Worker.Tick(default); h.Ai.Add(0,3); await h.Worker.Tick(default);
        var session=h.Worker.Get(camera.CameraId)!.SessionId;
        second=await h.Activate(secondZone.ZoneId,second); await h.Worker.Tick(default);
        Assert.Equal(0,h.Ai.Stops); Assert.Equal(2,h.Ai.Starts); Assert.Equal(session,h.Worker.Get(camera.CameraId)!.SessionId);
        h.Ai.Add(1000,3,0); h.Ai.Add(2000,3,2); h.Ai.Add(3000,3,2); await h.Worker.Tick(default);
        Assert.Equal(2,(await h.Rows<Incident>()).Length);
        var runtime=h.Worker.Get(camera.CameraId)!; Assert.Equal(3000,runtime.SourceElapsedMs); Assert.Equal(2,runtime.Zones.Length);
        Assert.Equal(first.UpdatedAt,runtime.Zones.Single(z=>z.ZoneId==zone.ZoneId).ConfigVersion);
        Assert.Equal(second.UpdatedAt,runtime.Zones.Single(z=>z.ZoneId==secondZone.ZoneId).ConfigVersion);
    }
    [Fact]
    public async Task DraftToIncidentEscalationEofViewerIndependenceRestartAndCooldown()
    {
        await using var h=new MonitoringRuntimeTestSupport(); await h.Initialize();
        var (camera,zone,draft)=await h.Configure(); var active=await h.Activate(zone.ZoneId,draft);
        await h.Worker.Tick(default); h.Ai.Add(0,1); h.Ai.Add(1000,1); await h.Worker.Tick(default);
        var incident=Assert.Single(await h.Rows<Incident>()); Assert.Equal("WARNING",incident.Severity); Assert.Equal("DETECTED",incident.Status);
        var url=$"/api/cameras/{camera.CameraId}";
        var attached=await MonitoringRuntimeTestSupport.Read<AiPreviewStatusView>(await h.Client.PostAsync(url+"/ai-preview/start",null)); Assert.Equal("MONITORING",attached.Purpose);
        await MonitoringRuntimeTestSupport.Read<AiPreviewStatusView>(await h.Client.PostAsync(url+"/ai-preview/stop",null)); Assert.Equal(0,h.Ai.ViewerStarts); Assert.Equal(0,h.Ai.ViewerStops); Assert.Equal(0,h.Ai.Stops);
        h.Ai.Add(2000,3); h.Ai.Add(3000,3); await h.Worker.Tick(default);
        incident=Assert.Single(await h.Rows<Incident>()); Assert.Equal("CRITICAL",incident.Severity);
        h.Ai.Add(4000,0); h.Ai.State="COMPLETED"; await h.Worker.Tick(default); await h.Worker.Tick(default);
        Assert.Equal(5,(await h.Rows<OperationalEvent>()).Length); Assert.Equal(1,h.Ai.Starts);
        Assert.Equal("CRITICAL",Assert.Single(await h.Rows<Incident>()).Severity); Assert.Null(Assert.Single(await h.Rows<Incident>()).ClosedAt);
        var feed=await MonitoringRuntimeTestSupport.Read<IncidentFeedView>(await h.Client.GetAsync(url+"/incidents")); var item=Assert.Single(feed.Items); Assert.True(item.IsDemo); Assert.Equal("PEOPLE_COUNT",item.MeasurementMode); Assert.Equal(0,item.MetricValue);
        var runtime=await MonitoringRuntimeTestSupport.Read<MonitoringCameraRuntimeView>(await h.Client.GetAsync(url+"/monitoring-runtime")); Assert.Equal("COMPLETED",runtime.State); Assert.Equal(0,Assert.Single(runtime.Zones).PeopleCount);
        // Closure is fixture SQL only, not a new product endpoint or auto-close behavior.
        h.Clock.UtcNow=new DateTime(h.Clock.UtcNow.Ticks/TimeSpan.TicksPerSecond*TimeSpan.TicksPerSecond,DateTimeKind.Utc).AddSeconds(1).AddMilliseconds(2);
        await h.Execute("UPDATE dbo.Incident SET status='CLOSED',closed_at=@closed WHERE incident_id=@id",("@closed",h.Clock.UtcNow),("@id",incident.IncidentId));
        Assert.Equal(h.Clock.UtcNow,Assert.Single(await h.Rows<Incident>()).ClosedAt);
        await h.Worker.Shutdown(default); h.Ai.State="LIVE";
        await h.Worker.Tick(default); h.Ai.Add(0,3); h.Ai.Add(1000,3); await h.Worker.Tick(default);
        Assert.Single(await h.Rows<Incident>()); Assert.Equal("COOLDOWN",Assert.Single(h.Worker.Get(camera.CameraId)!.Zones).Rules[0].Reason);
        h.Clock.UtcNow=h.Clock.UtcNow.AddSeconds(30); h.Ai.Add(2000,3); await h.Worker.Tick(default);
        Assert.Equal(2,(await h.Rows<Incident>()).Length);
        active=await h.Activate(zone.ZoneId,active,false);
        var stopping=await MonitoringRuntimeTestSupport.Read<MonitoringCameraRuntimeView>(await h.Client.GetAsync(url+"/monitoring-runtime"));
        Assert.Equal("STOPPING",stopping.State); Assert.Equal("MONITORING_STOP_PENDING",stopping.Reason);
        var tooEarly=await h.Client.PostAsJsonAsync($"/api/zones/{zone.ZoneId}/monitoring/activate",new MonitoringActivationRequest(active.UpdatedAt));
        Assert.Equal(System.Net.HttpStatusCode.Conflict,tooEarly.StatusCode);
        Assert.Contains("MONITORING_STOP_PENDING",await tooEarly.Content.ReadAsStringAsync());
        await h.Worker.Tick(default); Assert.False(h.Worker.IsMonitoringOwned(camera.CameraId));
        Assert.Equal("NO_ACTIVE_CONFIGURATION",(await MonitoringRuntimeTestSupport.Read<MonitoringCameraRuntimeView>(await h.Client.GetAsync(url+"/monitoring-runtime"))).Reason);
        var previousSession=attached.SessionId;
        await h.Activate(zone.ZoneId,active); await h.Worker.Tick(default);
        Assert.NotEqual(previousSession,h.Worker.Get(camera.CameraId)!.SessionId);
        h.Ai.Add(0,1); await h.Worker.Tick(default); Assert.Equal(0,h.Worker.Get(camera.CameraId)!.SourceElapsedMs);
    }

    [Fact]
    public async Task QueuedOldVersionCannotWriteAfterDeactivateAndSqlFeedPagesTiedHistory()
    {
        await using var h=new MonitoringRuntimeTestSupport(); await h.Initialize();
        var (camera,zone,draft)=await h.Configure(); var active=await h.Activate(zone.ZoneId,draft); await h.Worker.Tick(default);
        h.Ai.Add(0,3); await h.Worker.Tick(default); h.Ai.Add(1000,3);
        h.Ai.BeforeRead=()=>h.Execute("UPDATE dbo.MonitoringConfiguration SET status='INACTIVE',updated_at=DATEADD(ms,1,updated_at) WHERE config_id=@id",("@id",active.ConfigId)).GetAwaiter().GetResult();
        await h.Worker.Tick(default); Assert.Empty(await h.Rows<Incident>()); Assert.Single(await h.Rows<OperationalEvent>());
        var type=draft.Rules[0].IncidentTypeId;
        await h.Execute("""
          INSERT dbo.Incident(incident_id,incident_type_id,zone_id,trigger_camera_id,source_type,severity,title,status,closed_at,created_at,updated_at)
          SELECT NEWID(),@type,@zone,@camera,'AI_DETECTED','WARNING','History','CLOSED',@time,@time,@time
          FROM (SELECT TOP(1000) ROW_NUMBER() OVER(ORDER BY (SELECT NULL)) n FROM sys.all_objects a CROSS JOIN sys.all_objects b) x;
          """,("@type",type),("@zone",zone.ZoneId),("@camera",camera.CameraId),("@time",h.Clock.UtcNow));
        var root=$"/api/cameras/{camera.CameraId}/incidents";
        Assert.Empty((await MonitoringRuntimeTestSupport.Read<IncidentFeedView>(await h.Client.GetAsync(root))).Items);
        var seen=new HashSet<Guid>(); string path=root+"?includeEnded=true&limit=100";
        while(true) {
            var page=await MonitoringRuntimeTestSupport.Read<IncidentFeedView>(await h.Client.GetAsync(path)); Assert.InRange(page.Items.Length,1,100);
            foreach(var row in page.Items) Assert.True(seen.Add(row.IncidentId));
            if(!page.HasMore) break;
            path=root+$"?includeEnded=true&limit=100&afterCreatedAt={Uri.EscapeDataString(page.NextCreatedAt!.Value.ToString("O"))}&afterIncidentId={page.NextIncidentId}";
        }
        Assert.Equal(1000,seen.Count);
    }

    [Fact]
    public async Task ConcurrentSerializableWritersRetryToOneOpenIncidentAndIdempotentEvidence()
    {
        await using var h=new MonitoringRuntimeTestSupport(); await h.Initialize();
        var (_,zone,draft)=await h.Configure(); await h.Activate(zone.ZoneId,draft); await h.Worker.Tick(default);
        var snapshot=h.Ai.Snapshot; var z=snapshot.Zones[0]; var r=z.Rules[0];
        IncidentObservationCommand Command() {
            var batch=new AiMeasurementBatch(1,Guid.NewGuid(),Guid.NewGuid(),1,h.Clock.UtcNow,1000,snapshot.Fingerprint,[new(z.ZoneId,z.ConfigId,z.ConfigVersion,3,0)]);
            return new(snapshot,z,r,batch,batch.Zones[0],new(new(),"WARNING",1000,0,"THRESHOLD_SUSTAINED"),true);
        }
        var commands=new[] {Command(),Command()};
        async Task Write(IncidentObservationCommand cmd) {
            for(var attempt=0;;attempt++) {
                using var scope=h.Factory.Services.CreateScope();
                try { await scope.ServiceProvider.GetRequiredService<MonitoringIncidentWriter>().Apply(cmd,default); return; }
                catch(Supermarket.Application.ApplicationException e) when(attempt<3 && e.Code is "CONCURRENT_UPDATE" or "DATABASE_CONFLICT") { await Task.Delay(20); }
            }
        }
        await Task.WhenAll(commands.Select(Write)); await Write(commands[0]);
        Assert.Single(await h.Rows<Incident>()); Assert.Equal(2,(await h.Rows<OperationalEvent>()).Length);
    }
}
