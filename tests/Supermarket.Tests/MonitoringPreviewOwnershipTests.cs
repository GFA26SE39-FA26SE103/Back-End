using Supermarket.Application;
using Xunit;
namespace Supermarket.Tests;
public sealed class MonitoringPreviewOwnershipTests
{
    [Fact]
    public async Task ActiveMonitoringAttachesViewerAndViewerReleaseNeverStopsOwner()
    {
        var h=new MonitoringSetupTests.Harness(); h.AddRule(); h.Configuration.Status="ACTIVE";
        var client=new Client(); var preview=new AiPreview(h.Store,new MonitoringSetupTests.User("ADMIN"),client);
        var started=await preview.Start(h.Camera.CameraId,default);
        Assert.Equal("MONITORING",started.Purpose);
        await preview.Stop(h.Camera.CameraId,started.ViewerLeaseId!.Value,default);
        await preview.Start(h.Camera.CameraId,default,h.Zone.ZoneId);
        Assert.Equal(0,client.Starts); Assert.Equal(2,client.Attaches);
        Assert.Equal(1,client.Releases); Assert.Equal(0,client.ForceStops);
        h.Configuration.Status="DRAFT";
        var owned=new Owner(); preview=new(h.Store,new MonitoringSetupTests.User("ADMIN"),client,owned);
        var error=await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(()=>preview.Start(h.Camera.CameraId,default,h.Zone.ZoneId));
        Assert.Equal("AI_SESSION_MONITORING_OWNED",error.Code); Assert.Equal(0,client.ForceStops);
    }

    [Fact]
    public async Task ActiveSqlMonitoringWithoutWorkerReturnsStartPendingWithoutCreatingPreview()
    {
        var h=new MonitoringSetupTests.Harness(); h.AddRule(); h.Configuration.Status="ACTIVE";
        var client=new Client { WorkerRunning=false };
        var preview=new AiPreview(h.Store,new MonitoringSetupTests.User("ADMIN"),client);

        var error=await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(()=>preview.Start(h.Camera.CameraId,default));

        Assert.Equal("AI_MONITORING_START_PENDING",error.Code);
        Assert.Equal(1,client.Attaches); Assert.Equal(0,client.Starts); Assert.Equal(0,client.ForceStops);
    }

    [Fact]
    public async Task DraftConfigurationConflictPropagatesWithoutForceStoppingSharedWorker()
    {
        var h=new MonitoringSetupTests.Harness(); h.AddRule(); h.Configuration.Status="DRAFT";
        var client=new Client { StartError=new Supermarket.Application.ApplicationException("AI_PREVIEW_CONFIGURATION_CONFLICT","Different configuration.",409) };
        var preview=new AiPreview(h.Store,new MonitoringSetupTests.User("ADMIN"),client);

        var error=await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(()=>preview.Start(h.Camera.CameraId,default,h.Zone.ZoneId));

        Assert.Equal("AI_PREVIEW_CONFIGURATION_CONFLICT",error.Code);
        Assert.Equal(1,client.Starts); Assert.Equal(0,client.ForceStops);
    }
    private sealed class Owner:IMonitoringSessionOwnership { public bool IsMonitoringOwned(Guid c)=>true; }
    private sealed class Client:IAiPreviewClient {
        public int Starts,Attaches,Releases,ForceStops;
        public bool WorkerRunning=true;
        public Supermarket.Application.ApplicationException? StartError;
        private AiPreviewStatusView Status(Guid c,Guid? lease=null)=>new(c,"LIVE",null,DateTime.UtcNow,1,null,Guid.NewGuid(),"MONITORING",ViewerLeaseId:lease);
        public Task<AiPreviewStatusView> Start(Supermarket.Domain.CameraConnection c,CancellationToken ct,decimal? confidence=null) { Starts++; if(StartError is not null) throw StartError; return Task.FromResult(Status(c.CameraId,Guid.NewGuid())); }
        public Task<AiPreviewStatusView> AttachViewer(Guid c,CancellationToken ct) { Attaches++; if(!WorkerRunning) throw new Supermarket.Application.ApplicationException("AI_PREVIEW_NOT_RUNNING","AI preview is not running.",409); return Task.FromResult(Status(c,Guid.NewGuid())); }
        public Task<AiPreviewStatusView> ReleaseViewer(Guid c,Guid lease,CancellationToken ct) { Releases++; return Task.FromResult(Status(c)); }
        public Task<AiPreviewStatusView> ForceStop(Guid c,CancellationToken ct) { ForceStops++; return Task.FromResult(Status(c)); }
        public Task<AiPreviewStatusView> Status(Guid c,CancellationToken ct)=>Task.FromResult(Status(c));
        public Task<AiPreviewStatusView> Stop(Guid c,CancellationToken ct)=>Task.FromResult(Status(c));
        public Task<PreviewFrame> Frame(Guid c,CancellationToken ct)=>Task.FromResult(new PreviewFrame([1],"image/jpeg"));
        public Task<SequencedPreviewFrame?> NextFrame(Guid c,long afterSequence,Guid? afterSessionId,CancellationToken ct)=>throw new NotSupportedException();
    }
}
