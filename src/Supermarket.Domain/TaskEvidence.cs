namespace Supermarket.Domain;

/// <summary>dbo.TaskEvidence: one photo or the completion note of one Task submission (BR-26).</summary>
public sealed class TaskEvidence : Entity
{
    public Guid EvidenceId { get; set; }
    public Guid TaskId { get; set; }
    public Guid SubmittedByUserId { get; set; }
    public string EvidenceType { get; set; } = TaskEvidenceType.AfterPhoto;
    /// <summary>Storage token of the photo; null for the completion note.</summary>
    public string? EvidenceImage { get; set; }
    public string? Note { get; set; }
    public string ReviewStatus { get; set; } = TaskEvidenceReview.Draft;
    public string? RejectReason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public static class TaskEvidenceType
{
    public const string BeforePhoto = "BEFORE_PHOTO", AfterPhoto = "AFTER_PHOTO", CompletionNote = "COMPLETION_NOTE";
    public static readonly string[] Photos = [BeforePhoto, AfterPhoto];
}

/// <summary>
/// DRAFT: uploaded, not yet submitted (the Staff member can replace or remove it). PENDING: part of the
/// submission waiting for the Operator. VERIFIED / REJECTED: the Operator's decision on that submission.
/// </summary>
public static class TaskEvidenceReview
{
    public const string Draft = "DRAFT", Pending = "PENDING", Verified = "VERIFIED", Rejected = "REJECTED";
}

/// <summary>Strict photo requirements exposed to clients before a Task can be submitted (BR-26).</summary>
public sealed record EvidenceRequirement(bool BeforePhoto, bool AfterPhoto)
{
    /// <summary>
    /// Review 2 makes both photo types optional. Corrective Incident Tasks still require an after photo or a
    /// completion note; that alternative is enforced by the submission workflow rather than represented as a
    /// strict single-photo requirement here.
    /// </summary>
    public static EvidenceRequirement For(WorkTask task)
        => new(false, false);
}

public static class TaskEvidenceRules
{
    public const int MaxNoteLength = 2000;
    /// <summary>After this many rejected submissions by the same Staff member the Task goes to someone else (BR-28).</summary>
    public const int RejectionsBeforeReassignment = 2;
    /// <summary>Marks the assignment ended by the second rejection, so that Staff member is not offered or assigned the Task again.</summary>
    public const string RejectedTwiceReason = "REJECTED_TWICE";
}
