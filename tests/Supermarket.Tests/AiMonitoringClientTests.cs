using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.Ai;
using Xunit;
namespace Supermarket.Tests;
public sealed class AiMonitoringClientTests
{
    [Fact]
    public async Task StartsPrivateSnapshotAndReadsOrderedSourceTimeWithoutLeakingCredentials()
    {
        var camera=Guid.NewGuid(); var owner=Guid.NewGuid(); var session=Guid.NewGuid(); var zone=Guid.NewGuid(); var config=Guid.NewGuid(); var version=DateTime.Parse("2026-10-03T00:00:00Z").ToUniversalTime();
        var handler=new Handler(async r=> {
            Assert.Equal("key",r.Headers.GetValues("X-AI-Service-Key").Single());
            if(r.Method==HttpMethod.Post) {
                Assert.Equal($"/monitoring/sessions/{camera}/start",r.RequestUri!.AbsolutePath);
                using var body=JsonDocument.Parse(await r.Content!.ReadAsStringAsync());
                Assert.Equal(owner.ToString(),body.RootElement.GetProperty("owner_id").GetString());
                Assert.Equal("http://camera.test/video",body.RootElement.GetProperty("stream_url").GetString());
                Assert.Equal("password",body.RootElement.GetProperty("password").GetString());
                Assert.Equal(.8m,body.RootElement.GetProperty("zones")[0].GetProperty("confidence").GetDecimal());
                Assert.Equal(1,body.RootElement.GetProperty("protocol_version").GetInt32());
                return Json(new { camera_id=camera,state="STARTING",session_id=session,purpose="MONITORING",updated_at=version,frame_sequence=0 });
            }
            Assert.Equal(owner.ToString(),r.Headers.GetValues("X-AI-Monitoring-Owner").Single());
            Assert.Contains("limit=64",r.RequestUri!.Query);
            return Json(new { protocol_version=1,session_id=session,oldest_sequence=1,newest_sequence=1,gap=false,state="LIVE",batches=new[] {new { protocol_version=1,session_id=session,continuity_id=Guid.NewGuid(),sequence=1,captured_at=version,source_elapsed_ms=5000,configuration_fingerprint="f",zones=new[] {new { zone_id=zone,config_id=config,config_version=version,people_count=3,queue_count=1 }} }} });
        });
        var client=new AiMonitoringClient(new HttpClient(handler){BaseAddress=new("http://ai.test")},Options.Create(new AiPreviewOptions { InternalServiceKey="key" }),new Protector());
        var snapshot=new MonitoringCameraSnapshot(camera,new CameraConnection { CameraId=camera,SourceType="LIVE",Protocol="HTTP",StreamUri="http://camera.test/video",Username="viewer",CredentialSecretRef="encrypted" },"f",
            [new MonitoringZoneSnapshot(zone,"Queue",config,version,[new(0,0),new(1,0),new(0,1)],.8m,[new(Guid.NewGuid(),Guid.NewGuid(),"LONG_QUEUE","Queue","QUEUE_LENGTH","PEOPLE",1,2,1,300)])]);
        var started=await client.Start(snapshot,owner,default);
        Assert.Equal("MONITORING",started.Purpose);
        Assert.DoesNotContain("password",JsonSerializer.Serialize(started));
        var page=await client.Read(camera,owner,null,0,64,default);
        Assert.Equal(5000,Assert.Single(page.Batches).SourceElapsedMs);
        Assert.Equal(1,page.Batches[0].Zones[0].QueueCount);
    }
    [Fact]
    public async Task RejectsInvalidCountAndUnknownProtocolWithSanitizedError()
    {
        var handler=new Handler(_=>Task.FromResult(Json(new { protocol_version=2,session_id=Guid.NewGuid(),oldest_sequence=1,newest_sequence=0,gap=false,state="LIVE",batches=Array.Empty<object>() })));
        var client=new AiMonitoringClient(new HttpClient(handler){BaseAddress=new("http://ai.test")},Options.Create(new AiPreviewOptions()),new Protector());
        var error=await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(()=>client.Read(Guid.NewGuid(),Guid.NewGuid(),null,0,64,default));
        Assert.Equal("AI_MEASUREMENT_INVALID",error.Code);
    }
    private sealed class Protector:ICredentialProtector { public string Protect(string s)=>"encrypted"; public string Unprotect(string s)=>"password"; }
    [Theory]
    [InlineData(-1,0)]
    [InlineData(1,2)]
    public async Task RejectsInvalidAggregateCounts(int people,int queue)
    {
        var session=Guid.NewGuid(); var utc=DateTime.UtcNow;
        var handler=new Handler(_=>Task.FromResult(Json(new { protocol_version=1,session_id=session,oldest_sequence=1,newest_sequence=1,gap=false,state="LIVE",batches=new[] {new { protocol_version=1,session_id=session,continuity_id=Guid.NewGuid(),sequence=1,captured_at=utc,source_elapsed_ms=0,configuration_fingerprint="f",zones=new[] {new {zone_id=Guid.NewGuid(),config_id=Guid.NewGuid(),config_version=utc,people_count=people,queue_count=queue}}}}})));
        var client=new AiMonitoringClient(new HttpClient(handler){BaseAddress=new("http://ai.test")},Options.Create(new AiPreviewOptions()),new Protector());
        Assert.Equal("AI_MEASUREMENT_INVALID",(await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(()=>client.Read(Guid.NewGuid(),Guid.NewGuid(),null,0,64,default))).Code);
    }
    [Fact]
    public async Task RejectsContradictoryBufferRangeEvenWithNoBatches()
    {
        var handler=new Handler(_=>Task.FromResult(Json(new {protocol_version=1,session_id=Guid.NewGuid(),oldest_sequence=5,newest_sequence=1,gap=true,state="COMPLETED",batches=Array.Empty<object>()})));
        var client=new AiMonitoringClient(new HttpClient(handler){BaseAddress=new("http://ai.test")},Options.Create(new AiPreviewOptions()),new Protector());
        Assert.Equal("AI_MEASUREMENT_INVALID",(await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(()=>client.Read(Guid.NewGuid(),Guid.NewGuid(),null,0,64,default))).Code);
    }
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> respond):HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c)=>respond(r); }
    private static HttpResponseMessage Json(object body)=>new(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json") };
}
