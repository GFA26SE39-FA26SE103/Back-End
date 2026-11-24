using System.Text.Json;
using Supermarket.Domain;

namespace Supermarket.Application;

public sealed class AiPreview(ISetupStore store, ICurrentUser current, IAiPreviewClient client,IMonitoringSessionOwnership? ownership=null,
    CheckoutSourceGuard? checkoutGuard=null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AiPreviewStatusView> Start(Guid cameraId, CancellationToken ct, Guid? zoneId = null)
    {
        UseCase.LiveView(current);
        if (zoneId is not null) UseCase.Admin(current);
        var camera = UseCase.Found(await store.Find<Camera>(cameraId, ct));
        if (camera.Status != "ACTIVE")
            throw new ApplicationException("CAMERA_NOT_ACTIVE", "The camera must be active before AI preview can start.");

        var connection = await Connection(cameraId, ct);
        if (connection.LastTestedAt is null || connection.LastTestResult != "SUCCESS")
            throw new ApplicationException("CONNECTION_NOT_READY", "Test the current camera connection before AI preview.");
        if (!connection.IsEnabled)
            throw new ApplicationException("CONNECTION_DISABLED", "Enable the current camera connection before AI preview.");
        if (!(connection.SourceType == "LIVE" && connection.Protocol is "HTTP" or "RTSP" or "HLS")
            && !(connection.SourceType == "RECORDED" && connection.Protocol == "FILE"))
            throw new ApplicationException("AI_SOURCE_UNSUPPORTED", "AI preview supports live HTTP/RTSP/HLS or uploaded recorded video.");

        decimal? confidence = null;
        var owned=await Owned(cameraId,ct);
        if (zoneId is not null)
        {
            var zone = UseCase.Found(await store.Find<Zone>(zoneId.Value, ct));
            Rules.SameFloor(camera, zone);
            var checkoutMappings = zone.ZoneType == "CHECKOUT" && checkoutGuard is not null
                ? await checkoutGuard.MappingsForPreview(cameraId, zone.ZoneId, ct) : [];
            if (checkoutMappings.Length > 0)
            {
                foreach (var mapping in checkoutMappings)
                    ValidatePreviewRoi(mapping.LaneRoi, "Save a valid checkout Lane ROI before zone preview.");
            }
            else
            {
                var mapping = (await store.List<CameraZoneMapping>(m => m.CameraId == cameraId && m.ZoneId == zoneId.Value && m.Status == "ACTIVE", ct)).SingleOrDefault()
                    ?? throw new ApplicationException("ZONE_NOT_MAPPED", "Map this camera to the selected zone before testing its confidence.");
                ValidatePreviewRoi(mapping.RoiPolygon, "Save a valid camera-frame ROI before zone preview.");
            }
            var configuration = UseCase.Found((await store.List<MonitoringConfiguration>(c => c.ZoneId == zoneId.Value, ct)).SingleOrDefault());
            MonitoringPolicy.Confidence(configuration.ConfidenceThreshold);
            if(owned)
            {
                if(configuration.Status!="ACTIVE") throw new ApplicationException("AI_SESSION_MONITORING_OWNED","Deactivate monitoring before restarting this camera with Draft confidence.");
                return await Attach(cameraId,ct);
            }
            confidence = configuration.ConfidenceThreshold;
        }
        if(owned) return await Attach(cameraId,ct);
        return await client.Start(connection, ct, confidence);
    }

    private static void ValidatePreviewRoi(string polygon, string message)
    {
        try { Rules.Polygon(JsonSerializer.Deserialize<Point[]>(polygon, Json)); }
        catch (JsonException) { throw new ApplicationException("ROI_INVALID", message); }
    }

    public async Task<AiPreviewStatusView> Status(Guid cameraId, Guid viewerLeaseId, CancellationToken ct)
    {
        await Camera(cameraId, ct);
        return await client.Status(cameraId, viewerLeaseId, ct);
    }

    public async Task<PreviewFrame> Frame(Guid cameraId, Guid viewerLeaseId, CancellationToken ct)
    {
        await Camera(cameraId, ct);
        return await client.Frame(cameraId, viewerLeaseId, ct);
    }

    public async Task<SequencedPreviewFrame?> NextFrame(Guid cameraId, Guid viewerLeaseId, long afterSequence, Guid? afterSessionId, CancellationToken ct)
    {
        await Camera(cameraId, ct);
        return await client.NextFrame(cameraId, viewerLeaseId, afterSequence, afterSessionId, ct);
    }

    public async Task<AiPreviewStatusView> Stop(Guid cameraId, Guid viewerLeaseId, CancellationToken ct)
    {
        await Camera(cameraId, ct);
        return await client.ReleaseViewer(cameraId, viewerLeaseId, ct);
    }

    private async Task<bool> Owned(Guid cameraId,CancellationToken ct)
    {
        if(ownership?.IsMonitoringOwned(cameraId)==true) return true;
        if(checkoutGuard is not null && await checkoutGuard.IsActive(cameraId,null,ct)) return true;
        foreach(var mapping in await store.List<CameraZoneMapping>(m=>m.CameraId==cameraId && m.Status=="ACTIVE",ct))
            if((await store.List<MonitoringConfiguration>(c=>c.ZoneId==mapping.ZoneId && c.Status=="ACTIVE",ct)).Count>0) return true;
        return false;
    }
    private async Task<AiPreviewStatusView> Attach(Guid cameraId,CancellationToken ct)
    {
        try
        {
            var status=await client.AttachViewer(cameraId,ct);
            return status with { Purpose="MONITORING" };
        }
        catch(ApplicationException error) when(error.Code=="AI_PREVIEW_NOT_RUNNING")
        {
            throw new ApplicationException("AI_MONITORING_START_PENDING","AI monitoring is active but its worker is still starting.",409);
        }
    }

    private async Task<Camera> Camera(Guid cameraId, CancellationToken ct)
    {
        UseCase.LiveView(current);
        return UseCase.Found(await store.Find<Camera>(cameraId, ct));
    }

    private async Task<CameraConnection> Connection(Guid cameraId, CancellationToken ct)
        => UseCase.Found((await store.List<CameraConnection>(c => c.CameraId == cameraId, ct)).SingleOrDefault());
}
