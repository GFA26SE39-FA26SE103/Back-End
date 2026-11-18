using Supermarket.Domain;

namespace Supermarket.Application;

public sealed class CameraPlayback(ISetupStore store, ICurrentUser current, IMediaGateway gateway)
{
    public async Task<CameraPlaybackView> Prepare(Guid cameraId, CancellationToken ct)
    {
        UseCase.Viewer(current);
        var camera = UseCase.Found(await store.Find<Camera>(cameraId, ct));
        var connection = UseCase.Found((await store.List<CameraConnection>(c => c.CameraId == cameraId, ct)).SingleOrDefault());

        if (camera.Status != "ACTIVE" || !connection.IsEnabled || connection.LastTestResult != "SUCCESS" || connection.LastTestedAt is null)
            throw new ApplicationException("CONNECTION_NOT_READY", "An active camera with a tested and enabled connection is required.");

        Rules.Connection(connection);
        if (connection.SourceType != "LIVE" || connection.Protocol != "RTSP")
            throw new ApplicationException("PLAYBACK_UNSUPPORTED", "WebRTC playback currently requires a live RTSP source.", 422);

        var endpoint = await gateway.EnsurePlayback(connection, ct);
        return new(cameraId, endpoint.Transport, endpoint.Url);
    }
}
