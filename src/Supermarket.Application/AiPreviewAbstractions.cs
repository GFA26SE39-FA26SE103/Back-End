using Supermarket.Domain;

namespace Supermarket.Application;

public interface IAiPreviewClient
{
    Task<AiPreviewStatusView> Start(CameraConnection connection, CancellationToken ct, decimal? confidence = null);
    Task<AiPreviewStatusView> AttachViewer(Guid cameraId, CancellationToken ct)
        => throw new NotSupportedException();
    Task<AiPreviewStatusView> Status(Guid cameraId, Guid viewerLeaseId, CancellationToken ct)
        => throw new NotSupportedException();
    Task<PreviewFrame> Frame(Guid cameraId, Guid viewerLeaseId, CancellationToken ct)
        => throw new NotSupportedException();
    Task<SequencedPreviewFrame?> NextFrame(Guid cameraId, Guid viewerLeaseId, long afterSequence, Guid? afterSessionId, CancellationToken ct)
        => throw new NotSupportedException();
    Task<AiPreviewStatusView> ReleaseViewer(Guid cameraId, Guid viewerLeaseId, CancellationToken ct)
        => throw new NotSupportedException();
    Task<AiPreviewStatusView> ForceStop(Guid cameraId, CancellationToken ct)
        => throw new NotSupportedException();
}

