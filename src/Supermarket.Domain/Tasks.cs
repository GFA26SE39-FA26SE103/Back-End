namespace Supermarket.Domain;

/// <summary>dbo.Task. Named WorkTask so it never collides with System.Threading.Tasks.Task.</summary>
public sealed class WorkTask : TrackedEntity
{
    public Guid TaskId { get; set; }
    public Guid? IncidentId { get; set; }
    public Guid ZoneId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Priority { get; set; } = "WARNING";
    public string Status { get; set; } = WorkTaskStatus.Offered;
    public Guid? CurrentAssigneeUserId { get; set; }
    public Guid? AssignedByUserId { get; set; }
    public DateTime? AssignedAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public Guid? VerifiedByUserId { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string OriginType { get; set; } = TaskOrigin.Manual;
    public Guid? EventId { get; set; }
    public Guid? StaffReportId { get; set; }
    public DateTime? PlannedStartAt { get; set; }
    /// <summary>Rejected submissions of this Task across all performers (BR-28).</summary>
    public int RejectionCount { get; set; }
    public Guid? TaskTypeId { get; set; }
    public Guid? SlaPolicyId { get; set; }
    public string? SlaSnapshotJson { get; set; }
    public DateTime? ResponseDeadlineAt { get; set; }
    public DateTime? ResolutionDeadlineAt { get; set; }
    public string? VerificationMode { get; set; }
    public string? VerificationMethod { get; set; }
    public bool IsMandatory { get; set; }
}
/// <summary>V7 runtime offer. TaskAssignmentHistory remains an audit/assignment history, never the live offer queue.</summary>
public sealed class TaskOffer : Entity
{
    public Guid OfferId { get; set; }
    public Guid TaskId { get; set; }
    public Guid StaffUserId { get; set; }
    public Guid DispatchCycleId { get; set; }
    public int WaveNumber { get; set; }
    public string DispatchScope { get; set; } = TaskDispatchScope.SameZone;
    public string Status { get; set; } = TaskOfferStatus.Pending;
    public string? DeclineReasonCode { get; set; }
    public DateTime OfferedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RespondedAt { get; set; }
}
public sealed class TaskType : Entity
{
    public Guid TaskTypeId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public Guid RequiredSpecialtyId { get; set; }
    public string SourceCategory { get; set; } = "";
}
public sealed class StaffSpecialty : Entity
{
    public Guid SpecialtyId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
}
public sealed class TaskAssignmentHistory : Entity
{
    public Guid TaskAssignmentId { get; set; }
    public Guid TaskId { get; set; }
    public Guid StaffUserId { get; set; }
    public Guid? AssignedByUserId { get; set; }
    public string AssignmentType { get; set; } = TaskAssignmentType.Offer;
    public string Status { get; set; } = OfferStatus.Accepted;
    public DateTime AssignedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? Reason { get; set; }
}
public sealed class Shift : TrackedEntity
{
    public Guid ShiftId { get; set; }
    public Guid SupermarketId { get; set; }
    public string Name { get; set; } = "";
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public DateOnly WeekStart { get; set; }
    public string Status { get; set; } = "ACTIVE";
}
public sealed class ShiftAssignment : TrackedEntity
{
    public Guid ShiftAssignmentId { get; set; }
    public Guid UserId { get; set; }
    public Guid ShiftId { get; set; }
    public DateOnly WorkDate { get; set; }
    public DateTime? CheckInAt { get; set; }
    public DateTime? CheckOutAt { get; set; }
    public string Status { get; set; } = "SCHEDULED";
}
public sealed class StaffZoneAssignment : Entity
{
    public Guid StaffZoneAssignmentId { get; set; }
    public Guid ShiftAssignmentId { get; set; }
    public Guid ZoneId { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public DateTime AssignedAt { get; set; }
    public DateTime? UnassignedAt { get; set; }
}
public sealed class ResponsePolicy : TrackedEntity
{
    public Guid PolicyId { get; set; }
    public Guid RuleId { get; set; }
    public string EventPriority { get; set; } = "WARNING";
    public bool IsEnabled { get; set; } = true;
    public int Version { get; set; } = 1;
}
public sealed class ResponsePolicyAction : TrackedEntity
{
    public Guid ActionId { get; set; }
    public Guid PolicyId { get; set; }
    public string ActionType { get; set; } = "";
    public Guid? TargetRoleId { get; set; }
    public string ExecutionMode { get; set; } = "AUTOMATIC";
    public string? ConditionJson { get; set; }
    public string? ActionParametersJson { get; set; }
    public bool IsEnabled { get; set; } = true;
}
public sealed class Notification : Entity
{
    public Guid NotificationId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? IncidentId { get; set; }
    public Guid? CameraId { get; set; }
    public string NotificationType { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Severity { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? EventId { get; set; }
}
public sealed class NotificationRecipient : Entity
{
    public Guid NotificationRecipientId { get; set; }
    public Guid NotificationId { get; set; }
    public Guid UserId { get; set; }
    public string DeliveryChannel { get; set; } = "IN_APP";
    public string DeliveryStatus { get; set; } = "SENT";
    public string? RecipientReason { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
public sealed class SystemSetting : Entity
{
    public Guid SettingId { get; set; }
    public string SettingKey { get; set; } = "";
    public string SettingValue { get; set; } = "";
    public string ValueType { get; set; } = "STRING";
    public Guid? UpdatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Task lifecycle from the BR appendix B: Offered → Accepted → In progress → Submitted → Verified; Cancelled before Verified.</summary>
public static class WorkTaskStatus
{
    public const string Offered = "OFFERED", Accepted = "ACCEPTED", InProgress = "IN_PROGRESS", Submitted = "SUBMITTED", Verified = "VERIFIED", Cancelled = "CANCELLED";
    /// <summary>Tasks counted against the Staff workload limit.</summary>
    public static readonly string[] Active = [Accepted, InProgress];
}
public static class TaskOrigin
{
    public const string OperationalEvent = "OPERATIONAL_EVENT", Incident = "INCIDENT", StaffReport = "STAFF_REPORT", Manual = "MANUAL";
}
public static class TaskAssignmentType
{
    public const string Offer = "OFFER", Manual = "MANUAL";
}
/// <summary>Assignment-history status; live offer state belongs to TaskOfferStatus.</summary>
public static class OfferStatus
{
    public const string Accepted = "ACCEPTED", Declined = "DECLINED", Cancelled = "CANCELLED", Expired = "EXPIRED";
}
public static class TaskOfferStatus
{
    public const string Pending = "PENDING", Accepted = "ACCEPTED", Declined = "DECLINED", Expired = "EXPIRED", Cancelled = "CANCELLED";
    public static readonly string[] Active = [Pending];
}
public static class TaskDispatchScope
{
    // Zone expansion is deliberately not implemented until the ZoneAdjacency rule is approved.
    public const string SameZone = "SAME_ZONE";
}
public static class TaskResponse
{
    public const string HandleNow = "HANDLE_NOW", HandleLater = "HANDLE_LATER", CannotHandle = "CANNOT_HANDLE";
}
public static class TaskRules
{
    /// <summary>BR-23 default active-task limit per Staff member.</summary>
    public const int MaxActiveTasks = 2;
    /// <summary>Check-in opens this long before the shift starts.</summary>
    public static readonly TimeSpan CheckInOpensBefore = TimeSpan.FromMinutes(15);
    public const string RetryIntervalSetting = "task_offer.retry_interval_sec";
    public const string MaxRetryCountSetting = "task_offer.max_retry_count";
    /// <summary>BR-22 baseline when SystemSetting has no valid override: re-notify every 60 s, 3 attempts in total.</summary>
    public const int DefaultRetryIntervalSec = 60, DefaultMaxAttempts = 3;

    public static int Rank(string priority) => priority switch { "CRITICAL" => 2, "WARNING" => 1, _ => 0 };

    public static void Response(string? response, string? reason)
    {
        Rules.Status(response ?? "", TaskResponse.HandleNow, TaskResponse.HandleLater, TaskResponse.CannotHandle);
        if (response == TaskResponse.CannotHandle)
            Rules.Require(!string.IsNullOrWhiteSpace(reason), "REASON_REQUIRED", "A reason is required when the task cannot be handled.");
        Rules.Optional(reason, 200, "Reason");
    }
    /// <summary>A deferred start keeps responsibility with the Staff member; it must be in the future and inside the current shift.</summary>
    public static void PlannedStart(DateTime? plannedStartAt, DateTime now, DateTime shiftEndsAt)
        => Rules.Require(plannedStartAt is { } planned && planned > now && planned <= shiftEndsAt, "INVALID_PLANNED_START",
            "The planned start must be later than now and no later than the end of your shift.");
    public static void Accept(WorkTask task, DateTime now, string response, DateTime? plannedStartAt, Guid staffUserId)
    {
        task.CurrentAssigneeUserId = staffUserId;
        task.AssignedAt = now;
        task.AcceptedAt = now;
        if (response == TaskResponse.HandleNow)
        {
            task.Status = WorkTaskStatus.InProgress;
            task.StartedAt = now;
            task.PlannedStartAt = null;
        }
        else
        {
            task.Status = WorkTaskStatus.Accepted;
            task.PlannedStartAt = plannedStartAt;
        }
    }
}
