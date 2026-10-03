using Supermarket.Domain;

namespace Supermarket.Application;

public sealed class AiPreview(ISetupStore store, ICurrentUser current, IAiPreviewClient client)
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
            confidence = configuration.ConfidenceThreshold;
            // Python reuses a running camera session. Restart so the saved confidence is actually applied.
            await client.Stop(cameraId, ct);
        }
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
        return await client.Stop(cameraId, ct);
    }

    private async Task<Camera> Camera(Guid cameraId, CancellationToken ct)
    {
        UseCase.LiveView(current);
        return UseCase.Found(await store.Find<Camera>(cameraId, ct));
    }

    private async Task<CameraConnection> Connection(Guid cameraId, CancellationToken ct)
        => UseCase.Found((await store.List<CameraConnection>(c => c.CameraId == cameraId, ct)).SingleOrDefault());
}
