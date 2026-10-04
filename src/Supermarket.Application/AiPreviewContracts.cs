namespace Supermarket.Application;

public sealed record AiPreviewStatusView(
    Guid CameraId,
    string State,
    DateTime? StartedAt,
    DateTime UpdatedAt,
    long FrameSequence,
    string? ErrorCode,
    Guid? SessionId=null,
    string Purpose="PREVIEW",
    string? ConfigurationFingerprint=null,
    string? AnnotationContext=null);

public sealed record SequencedPreviewFrame(byte[] Bytes, string ContentType, long FrameSequence, Guid SessionId);

