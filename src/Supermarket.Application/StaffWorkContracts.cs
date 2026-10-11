namespace Supermarket.Application;

public sealed record ShiftView(Guid ShiftAssignmentId, Guid ShiftId, string ShiftName, DateOnly WorkDate, DateTime StartsAt, DateTime EndsAt,
    DateTime CheckInOpensAt, DateTime? CheckInAt, DateTime? CheckOutAt, string Status, ShiftZoneView[] Zones);
public sealed record ShiftZoneView(Guid ZoneId, string ZoneName);
public sealed record EligibilityView(bool CheckedIn, Guid? ShiftAssignmentId, DateTime? ShiftEndsAt, int ActiveTasks, int MaxActiveTasks, ShiftZoneView[] Zones);

/// <summary>One offer of one task to the calling Staff member (a TaskAssignmentHistory row of type OFFER).</summary>
public sealed record TaskOfferView(Guid OfferId, Guid TaskId, string Origin, string SourceCode, string? EventTypeCode, string Title, string? Instructions,
    string Priority, Guid ZoneId, string ZoneName, string? CameraCode, string? CameraName, decimal? MetricValue, string? MeasurementMode, string? MeasurementUnit,
    DateTime CreatedAt, int Recipients, int Attempt, int MaxAttempts, DateTime? NextAlertAt, string Status, string? ClosedReason, DateTime? EventClearedAt);
public sealed record TaskResponseRequest(string Response, DateTime? PlannedStartAt, string? Reason);
public sealed record TaskResponseResult(string Response, StaffTaskView? Task);

public sealed record StaffTaskView(Guid TaskId, string Origin, string SourceCode, string? EventTypeCode, string Title, string? Instructions, string Priority,
    string? RaisedFrom, Guid ZoneId, string ZoneName, string? CameraCode, string Status, string AssignmentType, Guid? AssigneeUserId, string? AssigneeName,
    DateTime CreatedAt, DateTime? AssignedAt, DateTime? AcceptedAt, DateTime? PlannedStartAt, DateTime? StartedAt, DateTime? SubmittedAt,
    DateTime? EventClearedAt, bool NeedsAssignment, DateTime UpdatedAt);

public sealed record NotificationView(Guid NotificationId, string Type, string Title, string Message, string? Severity, Guid? TaskId, Guid? EventId,
    DateTime CreatedAt, DateTime? ReadAt);

public sealed record TaskAssignRequest(Guid StaffUserId, string? Reason);
public sealed record AssignableStaffView(Guid UserId, string FullName, bool AssignedToZone, int ActiveTasks, DateTime ShiftEndsAt);
