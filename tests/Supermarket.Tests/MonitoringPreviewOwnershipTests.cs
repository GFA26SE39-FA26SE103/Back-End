using Supermarket.Application;
using Xunit;
namespace Supermarket.Tests;
public sealed class MonitoringPreviewOwnershipTests
{
    [Fact]
    public async Task DatabaseActiveGuardProtectsPreviewEvenBeforeWorkerPopulatesOwnership()
    {
        var h=new MonitoringSetupTests.Harness(); h.AddRule(); h.Configuration.Status="ACTIVE";
        var client=new Client(); var preview=new AiPreview(h.Store,new MonitoringSetupTests.User("ADMIN"),client);
        Assert.Equal("MONITORING",(await preview.Start(h.Camera.CameraId,default)).Purpose);
        await preview.Stop(h.Camera.CameraId,default);
        await preview.Start(h.Camera.CameraId,default,h.Zone.ZoneId);
        Assert.Equal(0,client.Starts); Assert.Equal(0,client.Stops);
        h.Configuration.Status="DRAFT";
        var owned=new Owner(); preview=new(h.Store,new MonitoringSetupTests.User("ADMIN"),client,owned);
        var error=await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(()=>preview.Start(h.Camera.CameraId,default,h.Zone.ZoneId));
        Assert.Equal("AI_SESSION_MONITORING_OWNED",error.Code); Assert.Equal(0,client.Stops);
    }
    private sealed class Owner:IMonitoringSessionOwnership { public bool IsMonitoringOwned(Guid c)=>true; }
    private sealed class Client:IAiPreviewClient {
        public int Starts,Stops;
        private AiPreviewStatusView Status(Guid c)=>new(c,"LIVE",null,DateTime.UtcNow,1,null,Guid.NewGuid(),"MONITORING");
        public Task<AiPreviewStatusView> Start(Supermarket.Domain.CameraConnection c,CancellationToken ct,decimal? confidence=null) { Starts++; return Task.FromResult(Status(c.CameraId)); }
        public Task<AiPreviewStatusView> Status(Guid c,CancellationToken ct)=>Task.FromResult(Status(c));
        public Task<AiPreviewStatusView> Stop(Guid c,CancellationToken ct) { Stops++; return Task.FromResult(Status(c)); }
        public Task<PreviewFrame> Frame(Guid c,CancellationToken ct)=>Task.FromResult(new PreviewFrame([1],"image/jpeg"));
    }
}
