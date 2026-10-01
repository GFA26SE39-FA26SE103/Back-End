namespace Supermarket.Application;

public sealed record AiPreviewStatusView(
    Guid CameraId,
    string State,
    DateTime? StartedAt,
    DateTime UpdatedAt,
    long FrameSequence,
    string? ErrorCode);

