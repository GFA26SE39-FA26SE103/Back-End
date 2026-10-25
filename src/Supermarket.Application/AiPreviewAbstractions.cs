using Supermarket.Domain;

namespace Supermarket.Application;

public interface IAiPreviewClient
{
    Task<AiPreviewStatusView> Start(CameraConnection connection, CancellationToken ct, decimal? confidence = null);
    Task<AiPreviewStatusView> AttachViewer(Guid cameraId, CancellationToken ct)
        => Status(cameraId, ct);
    Task<AiPreviewStatusView> Status(Guid cameraId, Guid viewerLeaseId, CancellationToken ct)
        => Status(cameraId, ct);
    Task<PreviewFrame> Frame(Guid cameraId, Guid viewerLeaseId, CancellationToken ct)
        => Frame(cameraId, ct);
    Task<SequencedPreviewFrame?> NextFrame(Guid cameraId, Guid viewerLeaseId, long afterSequence, Guid? afterSessionId, CancellationToken ct)
        => NextFrame(cameraId, afterSequence, afterSessionId, ct);
    Task<AiPreviewStatusView> ReleaseViewer(Guid cameraId, Guid viewerLeaseId, CancellationToken ct)
        => Stop(cameraId, ct);
    Task<AiPreviewStatusView> ForceStop(Guid cameraId, CancellationToken ct)
        => Stop(cameraId, ct);

    // Removed after the public API/use case migrates to viewer-scoped leases in Task 4.
    Task<AiPreviewStatusView> Status(Guid cameraId, CancellationToken ct);
    Task<PreviewFrame> Frame(Guid cameraId, CancellationToken ct);
    Task<SequencedPreviewFrame?> NextFrame(Guid cameraId, long afterSequence, Guid? afterSessionId, CancellationToken ct);
    Task<AiPreviewStatusView> Stop(Guid cameraId, CancellationToken ct);
}

