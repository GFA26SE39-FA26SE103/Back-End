namespace Supermarket.Application;

public sealed record WorkShiftView(Guid ShiftId, DateOnly WeekStart, string Name, TimeOnly StartTime, TimeOnly EndTime, double Hours, string Status, bool InUse);
public sealed record CreateWorkShiftRequest(DateOnly WeekStart, string Name, TimeOnly StartTime, TimeOnly EndTime);
/// <summary>Times can change only while no live assignment uses this weekly Work Shift.</summary>
public sealed record UpdateWorkShiftRequest(string? Name, TimeOnly? StartTime, TimeOnly? EndTime, string? Status);
public sealed record CopyWorkShiftWeekRequest(DateOnly SourceWeekStart, DateOnly TargetWeekStart);

public sealed record ScheduledShiftView(Guid ShiftAssignmentId, Guid UserId, string FullName, Guid ShiftId, string ShiftName, DateOnly WorkDate,
    DateTime StartsAt, DateTime EndsAt, string Status, DateTime? CheckInAt, ShiftZoneView[] Zones, bool Editable, Guid? PendingSwapRequestId, DateTime UpdatedAt);
/// <summary>BR-02 coverage of one Zone in one shift on one day. Planned counts DRAFT and SCHEDULED rows; Published counts SCHEDULED only.</summary>
public sealed record ZoneCoverageView(DateOnly WorkDate, Guid ShiftId, string ShiftName, Guid ZoneId, string ZoneName, int MinStaff, int Planned, int Published, bool BelowMinimum);
/// <summary>Manager-owned staffing baseline. It applies to this Zone in every active Work Shift.</summary>
public sealed record ZoneMinimumStaffRequest(int MinStaff, DateTime? ExpectedUpdatedAt = null);
public sealed record ScheduleView(DateOnly From, DateOnly To, ScheduledShiftView[] Assignments, ZoneCoverageView[] Coverage, int DraftCount, int GapCount);

public sealed record StaffOptionView(Guid UserId, string FullName, bool Eligible, double WeeklyHoursAfter, string[] Problems);
public sealed record AssignShiftRequest(Guid UserId, Guid ShiftId, DateOnly WorkDate, Guid[]? ZoneIds);
/// <summary>Change the person and/or replace the single Zone of one assignment; omitted fields stay unchanged.</summary>
public sealed record UpdateAssignmentRequest(Guid? UserId, Guid[]? ZoneIds);
public sealed record CancelAssignmentRequest(string? Reason);
public sealed record PublishScheduleRequest(DateOnly From, DateOnly To);
public sealed record PublishScheduleResult(DateOnly From, DateOnly To, int Published, int Notified);

public sealed record UnavailabilityView(Guid UnavailabilityId, Guid UserId, string FullName, DateTime StartAt, DateTime EndAt, string? Note, DateTime CreatedAt);
public sealed record UnavailabilityRequest(Guid UserId, DateTime StartAt, DateTime EndAt, string? Note);
