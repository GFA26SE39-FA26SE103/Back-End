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
    public const string BeforePhoto = "BEFORE_PHOTO", AfterPhoto = "AFTER_PHOTO", CompletionNote = "COMPLETION_NOTE",
        MissingPhotoReason = "MISSING_PHOTO_REASON";
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
    public const int MaxMissingPhotoReasonLength = 1000;
    /// <summary>After this many rejected submissions by the same Staff member the Task goes to someone else (BR-28).</summary>
    public const int RejectionsBeforeReassignment = 2;
    /// <summary>Marks the assignment ended by the second rejection, so that Staff member is not offered or assigned the Task again.</summary>
    public const string RejectedTwiceReason = "REJECTED_TWICE";
    /// <summary>Marks the replacement Staff assignment whose first submission was rejected; further automatic reassignment stops.</summary>
    public const string RejectedAfterReassignmentReason = "REJECTED_AFTER_REASSIGNMENT";
    public static readonly string[] ReassignmentExclusionReasons = [RejectedTwiceReason, RejectedAfterReassignmentReason];
}

public static class TaskVerificationMode
{
    public const string Auto = "AUTO", Manual = "MANUAL";
}

public static class TaskVerificationMethod
{
    public const string OperationalMetric = "OPERATIONAL_EVENT_METRIC";
    public const string OperatorEvidence = "OPERATOR_EVIDENCE";
    public const string MetricException = "OPERATOR_METRIC_EXCEPTION";
}

public static class TaskVerificationRules
{
    private static readonly string[] MetricTaskTypes = ["TT-01", "TT-02", "TT-03", "TT-04", "TT-05"];
    private static readonly string[] AlwaysManualTaskTypes = ["TT-07", "TT-12", "TT-13", "TT-14", "TT-15"];

    /// <summary>Snapshots the verification lane once; later dispatch/rework must not silently relax it.</summary>
    public static void Initialize(WorkTask task, TaskType? taskType, string? slaPriorityCode = null)
    {
        if (!string.IsNullOrWhiteSpace(task.VerificationMode)) return;
        var code = taskType?.Code?.Trim().ToUpperInvariant();
        var priority = slaPriorityCode?.Trim().ToUpperInvariant();
        var manual = priority is not ("LOW" or "MEDIUM" or "HIGH" or "CRITICAL")
            || priority == "CRITICAL"
            || task.OriginType != TaskOrigin.OperationalEvent
            || code is null || AlwaysManualTaskTypes.Contains(code) || !MetricTaskTypes.Contains(code);
        task.VerificationMode = manual ? TaskVerificationMode.Manual : TaskVerificationMode.Auto;
        task.VerificationMethod = manual ? TaskVerificationMethod.OperatorEvidence : TaskVerificationMethod.OperationalMetric;
    }
}
