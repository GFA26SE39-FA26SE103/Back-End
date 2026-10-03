using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.Monitoring;
using Xunit;
namespace Supermarket.Tests;

// Only this fixture's random local SQL database is modified. Controlled Tick uses
// the production coordinator while avoiding a background timing race in assertions.
internal sealed class MonitoringRuntimeTestSupport : IAsyncDisposable
{
    public SqlApiFixture Sql { get; }=new();
    public WebApplicationFactory<Program> Factory=null!;
    public HttpClient Client=null!;
    public RuntimeAi Ai { get; }=new();
    public RuntimeClock Clock { get; }=new();
    public MonitoringCameraCoordinator Worker=>Factory.Services.GetRequiredService<MonitoringCameraCoordinator>();
    public static Point[] Roi=>[new(0,0),new(1,0),new(0,1)];
    public async Task Initialize()
    {
        await Sql.InitializeAsync();
        Factory=Sql.Factory.WithWebHostBuilder(b=>b.ConfigureTestServices(s=> {
            s.RemoveAll<IAiMonitoringClient>(); s.AddSingleton<IAiMonitoringClient>(Ai);
            s.RemoveAll<IAiPreviewClient>(); s.AddSingleton<IAiPreviewClient>(Ai);
            s.RemoveAll<ICameraStream>(); s.AddSingleton<ICameraStream>(new Probe());
            s.RemoveAll<IClock>(); s.AddSingleton<IClock>(Clock);
        }));
        Client=Factory.CreateClient();
        var login=await Read<LoginResponse>(await Client.PostAsJsonAsync("/api/auth/login",new LoginRequest(SqlApiFixture.AdminEmail,SqlApiFixture.AdminPassword)));
        Client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",login.AccessToken);
    }
    public async Task<(Camera Camera,ZoneView Zone,MonitoringConfigurationView Config)> Configure(bool queue=false)
    {
        var stores=(await Client.GetFromJsonAsync<JsonElement[]>("/api/supermarkets"))!;
        var supermarket=stores.Length==0?(await Read<JsonElement>(await Client.PostAsJsonAsync("/api/supermarkets",new StoreRequest("TEST","Runtime test",null)))).GetProperty("supermarketId").GetGuid():stores[0].GetProperty("supermarketId").GetGuid();
        var floor=await Read<Floor>(await Client.PostAsJsonAsync($"/api/supermarkets/{supermarket}/floors",new FloorRequest(Random.Shared.Next(1,1000000),"Runtime floor",null,null,null)));
        var zone=await Read<ZoneView>(await Client.PostAsJsonAsync($"/api/floors/{floor.FloorId}/zones",new ZoneRequest(Guid.NewGuid().ToString("N"),"ROI zone","QUEUE",Roi)));
        var camera=await Read<Camera>(await Client.PostAsJsonAsync($"/api/floors/{floor.FloorId}/cameras",new CameraRequest(Guid.NewGuid().ToString("N"),"Runtime camera",null,null,null,Clock.UtcNow,Clock.UtcNow.AddYears(1),.1m,.2m,0,"ACTIVE")));
        var connection=$"/api/cameras/{camera.CameraId}/connection";
        await Read<ConnectionView>(await Client.PutAsJsonAsync(connection,new ConnectionRequest("LIVE","HTTP","http://runtime.test/video")));
        await Read<ConnectionView>(await Client.PostAsync(connection+"/test",null)); await Read<ConnectionView>(await Client.PostAsync(connection+"/enable",null));
        // A recorded URI in an isolated fixture, with fake AI; no uploaded/user video is changed.
        await Execute("UPDATE dbo.CameraConnection SET source_type='RECORDED',protocol='FILE',stream_uri='file:///C:/runtime-test/video.mp4' WHERE camera_id=@camera",("@camera",camera.CameraId));
        await Read<MappingView>(await Client.PutAsJsonAsync($"/api/cameras/{camera.CameraId}/zones/{zone.ZoneId}",new MappingRequest(Roi)));
        var type=(await Client.GetFromJsonAsync<IncidentTypeView[]>("/api/incident-types"))!.Single(t=>t.Code==(queue?"LONG_QUEUE":"OVERCROWDING_CONGESTION"));
        var config=await Read<MonitoringConfigurationView>(await Client.PutAsJsonAsync($"/api/zones/{zone.ZoneId}/monitoring",new MonitoringRequest("Runtime config",.7m,[new(type.IncidentTypeId,1,2,"PEOPLE",1,30,true,queue?null:"{\"measurementMode\":\"PEOPLE_COUNT\"}")])));
        var review=await Read<MonitoringReviewView>(await Client.GetAsync($"/api/zones/{zone.ZoneId}/monitoring/review")); Assert.True(review.CanActivate); Assert.Null(zone.AreaM2);
        return(camera,zone,config);
    }
    public async Task<MonitoringConfigurationView> Activate(Guid zoneId,MonitoringConfigurationView config,bool active=true)=>await Read<MonitoringConfigurationView>(await Client.PostAsJsonAsync($"/api/zones/{zoneId}/monitoring/{(active?"activate":"deactivate")}",new MonitoringActivationRequest(config.UpdatedAt)));
    public async Task Execute(string sql,params (string Key,object Value)[] parameters)
    {
        await using var c=new SqlConnection(Sql.ConnectionString); await c.OpenAsync(); await using var cmd=new SqlCommand(sql,c);
        foreach(var p in parameters)
        {
            // AddWithValue(DateTime) infers legacy datetime (3.33ms steps), even
            // when the destination is datetime2(3). Fixture timestamps must use
            // the same exact millisecond precision as the production EF mapping.
            if(p.Value is DateTime time) { var value=cmd.Parameters.Add(p.Key,System.Data.SqlDbType.DateTime2); value.Scale=3; value.Value=time; }
            else cmd.Parameters.AddWithValue(p.Key,p.Value);
        }
        await cmd.ExecuteNonQueryAsync();
    }
    public async Task<T[]> Rows<T>() where T:Entity,new() { using var scope=Factory.Services.CreateScope(); return (await scope.ServiceProvider.GetRequiredService<ISetupStore>().List<T>()).ToArray(); }
    public static async Task<T> Read<T>(HttpResponseMessage response) { var text=await response.Content.ReadAsStringAsync(); Assert.True(response.IsSuccessStatusCode,$"{response.StatusCode}: {text}"); return JsonSerializer.Deserialize<T>(text,new JsonSerializerOptions(JsonSerializerDefaults.Web))!; }
    public async ValueTask DisposeAsync() { Client?.Dispose(); if(Factory is not null) await Factory.DisposeAsync(); await Sql.DisposeAsync(); }
    internal sealed class RuntimeClock:IClock {
        // SQL datetime2(3) precision; the exact cooldown boundary is not a
        // sub-millisecond rounding test of an artificial closure timestamp.
        public DateTime UtcNow {get;set;}=new(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond,DateTimeKind.Utc);
    }
    private sealed class Probe:ICameraStream { public Task<ProbeResult> Test(CameraConnection c,CancellationToken ct)=>Task.FromResult(new ProbeResult(true,"FRAME_RECEIVED")); public Task<PreviewFrame> Preview(CameraConnection c,CancellationToken ct)=>Task.FromResult(new PreviewFrame([1],"image/jpeg")); }
    internal sealed class RuntimeAi:IAiMonitoringClient,IAiPreviewClient
    {
        public MonitoringCameraSnapshot Snapshot=null!; public Guid Session=Guid.NewGuid(),Epoch=Guid.NewGuid();
        public int Starts,Stops,ViewerStarts,ViewerStops; public string State="LIVE"; public List<AiMeasurementBatch> Batches=[];
        public Action? BeforeRead;
        public void Add(long ms,int people,int queue=0)=>Batches.Add(new(1,Session,Epoch,Batches.Count+1,DateTime.UtcNow,ms,Snapshot.Fingerprint,Snapshot.Zones.Select(z=>new AiZoneMeasurement(z.ZoneId,z.ConfigId,z.ConfigVersion,people,queue)).ToArray()));
        private AiPreviewStatusView Status(Guid id)=>new(id,State,null,DateTime.UtcNow,Batches.Count,null,Session,"MONITORING",Snapshot?.Fingerprint,"confidence:0.7");
        public Task<AiPreviewStatusView> Start(MonitoringCameraSnapshot s,Guid owner,CancellationToken ct) { Snapshot=s; Starts++; return Task.FromResult(Status(s.CameraId)); }
        public Task<AiMeasurementPage> Read(Guid c,Guid owner,Guid? session,long after,int limit,CancellationToken ct) { BeforeRead?.Invoke(); BeforeRead=null; return Task.FromResult(new AiMeasurementPage(1,Session,1,Batches.LastOrDefault()?.Sequence??0,false,Batches.Where(b=>b.Sequence>after).Take(limit).ToArray(),State,null)); }
        public Task Stop(Guid c,Guid owner,CancellationToken ct) { Stops++; Batches.Clear(); Session=Guid.NewGuid(); Epoch=Guid.NewGuid(); return Task.CompletedTask; }
        public Task<AiPreviewStatusView> Start(CameraConnection c,CancellationToken ct,decimal? confidence=null) { ViewerStarts++; return Task.FromResult(Status(c.CameraId)); }
        public Task<AiPreviewStatusView> Status(Guid c,CancellationToken ct)=>Task.FromResult(Status(c));
        public Task<AiPreviewStatusView> Stop(Guid c,CancellationToken ct) { ViewerStops++; return Task.FromResult(Status(c)); }
        public Task<PreviewFrame> Frame(Guid c,CancellationToken ct)=>Task.FromResult(new PreviewFrame([1],"image/jpeg"));
        public Task<SequencedPreviewFrame?> NextFrame(Guid c,long afterSequence,Guid? afterSessionId,CancellationToken ct)=>throw new NotSupportedException();
    }
}
