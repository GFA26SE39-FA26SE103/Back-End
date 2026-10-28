namespace Supermarket.Application;

/// <summary>Evidence of one Task: what is required, the current draft, and every submission with its review.</summary>
public sealed record TaskEvidenceView(Guid TaskId, string TaskStatus, bool RequiresBeforePhoto, bool RequiresAfterPhoto, int RejectionCount,
    TaskEvidencePhotoView[] Drafts, TaskSubmissionView[] Submissions);

/// <summary>One photo. <c>ImageUrl</c> is the authenticated API route that streams it.</summary>
public sealed record TaskEvidencePhotoView(Guid EvidenceId, string EvidenceType, string ImageUrl, DateTime CapturedAt);

/// <summary>A submitted completion record: the note, its photos and the Operator's decision.</summary>
public sealed record TaskSubmissionView(Guid SubmissionId, Guid SubmittedByUserId, string? SubmittedByName, string Note, DateTime SubmittedAt,
    string ReviewStatus, string? RejectReason, TaskEvidencePhotoView[] Photos);

public sealed record TaskSubmitRequest(string Note, DateTime? ExpectedUpdatedAt = null);
public sealed record TaskVerifyRequest(DateTime? ExpectedUpdatedAt = null);
public sealed record TaskRejectRequest(string Reason, DateTime? ExpectedUpdatedAt = null);

public sealed record StoredEvidence(string Token, string ContentType);
public sealed record EvidenceFile(Stream Content, string ContentType);

/// <summary>Stores Task evidence photos outside the database; the row keeps only the returned token.</summary>
public interface ITaskEvidenceStorage
{
    /// <summary>Validates format, size and file signature before anything is kept.</summary>
    Task<StoredEvidence> Save(Guid taskId, Stream content, string filename, string contentType, long length, CancellationToken ct);
    Task<EvidenceFile?> Open(Guid taskId, string token, CancellationToken ct);
    Task Delete(Guid taskId, string token, CancellationToken ct);
}
