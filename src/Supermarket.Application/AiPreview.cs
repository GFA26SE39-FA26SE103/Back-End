using Supermarket.Domain;

namespace Supermarket.Application;

public sealed class AiPreview(ISetupStore store, ICurrentUser current, IAiPreviewClient client)
{
    public async Task<AiPreviewStatusView> Start(Guid cameraId, CancellationToken ct)
    {
        UseCase.Admin(current);
        var camera = UseCase.Found(await store.Find<Camera>(cameraId, ct));
        if (camera.Status != "ACTIVE")
            throw new ApplicationException("CAMERA_NOT_ACTIVE", "The camera must be active before AI preview can start.");

        var connection = await Connection(cameraId, ct);
        if (connection.LastTestedAt is null || connection.LastTestResult != "SUCCESS")
            throw new ApplicationException("CONNECTION_NOT_READY", "Test the current camera connection before AI preview.");
        if (!connection.IsEnabled)
            throw new ApplicationException("CONNECTION_DISABLED", "Enable the current camera connection before AI preview.");
        if (connection.SourceType != "LIVE" || connection.Protocol is not ("HTTP" or "RTSP" or "HLS"))
            throw new ApplicationException("AI_SOURCE_UNSUPPORTED", "AI preview supports live HTTP, RTSP, or HLS camera streams only.");

        return await client.Start(connection, ct);
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
        UseCase.Admin(current);
        return UseCase.Found(await store.Find<Camera>(cameraId, ct));
    }

    private async Task<CameraConnection> Connection(Guid cameraId, CancellationToken ct)
        => UseCase.Found((await store.List<CameraConnection>(c => c.CameraId == cameraId, ct)).SingleOrDefault());
}
