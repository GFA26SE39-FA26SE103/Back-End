using Supermarket.Domain;

namespace Supermarket.Application;

public sealed class AiPreview(ISetupStore store, ICurrentUser current, IAiPreviewClient client,IMonitoringSessionOwnership? ownership=null)
{
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
            var mapping = (await store.List<CameraZoneMapping>(m => m.CameraId == cameraId && m.ZoneId == zoneId.Value && m.Status == "ACTIVE", ct)).SingleOrDefault()
                ?? throw new ApplicationException("ZONE_NOT_MAPPED", "Map this camera to the selected zone before testing its confidence.");
            try { Rules.Polygon(System.Text.Json.JsonSerializer.Deserialize<Point[]>(mapping.RoiPolygon)); }
            catch (System.Text.Json.JsonException) { throw new ApplicationException("ROI_INVALID", "Save a valid camera-frame ROI before zone preview."); }
            var configuration = UseCase.Found((await store.List<MonitoringConfiguration>(c => c.ZoneId == zoneId.Value, ct)).SingleOrDefault());
            MonitoringPolicy.Confidence(configuration.ConfidenceThreshold);
            if(owned)
            {
                if(configuration.Status!="ACTIVE") throw new ApplicationException("AI_SESSION_MONITORING_OWNED","Deactivate monitoring before restarting this camera with Draft confidence.");
                return await Attach(cameraId,ct);
            }
            confidence = configuration.ConfidenceThreshold;
            // Python reuses a running camera session. Restart so the saved confidence is actually applied.
            await client.Stop(cameraId, ct);
        }
        if(owned) return await Attach(cameraId,ct);
        return await client.Start(connection, ct, confidence);
    }

    public async Task<AiPreviewStatusView> Status(Guid cameraId, CancellationToken ct)
    {
        await Camera(cameraId, ct);
        return await client.Status(cameraId, ct);
    }

    public async Task<PreviewFrame> Frame(Guid cameraId, CancellationToken ct)
    {
        await Camera(cameraId, ct);
        return await client.Frame(cameraId, ct);
    }

    public async Task<AiPreviewStatusView> Stop(Guid cameraId, CancellationToken ct)
    {
        await Camera(cameraId, ct);
        if(await Owned(cameraId,ct)) return await Attach(cameraId,ct);
        return await client.Stop(cameraId, ct);
    }

    private async Task<bool> Owned(Guid cameraId,CancellationToken ct)
    {
        if(ownership?.IsMonitoringOwned(cameraId)==true) return true;
        foreach(var mapping in await store.List<CameraZoneMapping>(m=>m.CameraId==cameraId && m.Status=="ACTIVE",ct))
            if((await store.List<MonitoringConfiguration>(c=>c.ZoneId==mapping.ZoneId && c.Status=="ACTIVE",ct)).Count>0) return true;
        return false;
    }
    private async Task<AiPreviewStatusView> Attach(Guid cameraId,CancellationToken ct)
    {
        var status=await client.Status(cameraId,ct);
        return status with { Purpose="MONITORING",State=status.State=="STOPPED"?"STARTING":status.State };
    }

    private async Task<Camera> Camera(Guid cameraId, CancellationToken ct)
    {
        UseCase.LiveView(current);
        return UseCase.Found(await store.Find<Camera>(cameraId, ct));
    }

    private async Task<CameraConnection> Connection(Guid cameraId, CancellationToken ct)
        => UseCase.Found((await store.List<CameraConnection>(c => c.CameraId == cameraId, ct)).SingleOrDefault());
}
