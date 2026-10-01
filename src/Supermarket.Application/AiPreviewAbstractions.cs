using Supermarket.Domain;

namespace Supermarket.Application;

public interface IAiPreviewClient
{
    Task<AiPreviewStatusView> Start(CameraConnection connection, CancellationToken ct);
    Task<AiPreviewStatusView> Status(Guid cameraId, CancellationToken ct);
    Task<PreviewFrame> Frame(Guid cameraId, CancellationToken ct);
    Task<AiPreviewStatusView> Stop(Guid cameraId, CancellationToken ct);
}

