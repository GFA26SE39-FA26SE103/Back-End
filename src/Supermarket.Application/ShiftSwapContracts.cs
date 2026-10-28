namespace Supermarket.Application;

public sealed record SwapPersonView(Guid UserId, string FullName);
/// <summary>One shift assignment as it is now (before any approved change).</summary>
public sealed record SwapShiftView(Guid ShiftAssignmentId, Guid UserId, string ShiftName, DateOnly WorkDate, DateTime StartsAt, DateTime EndsAt, ShiftZoneView[] Zones);
/// <summary>One rule evaluated for the request as it would be applied now.</summary>
public sealed record SwapCheckView(string Code, bool Passed, string Message);

public sealed record SwapRequestView(Guid SwapRequestId, string Type, string Status, SwapPersonView Requester, SwapShiftView RequesterShift,
    SwapPersonView? Target, SwapShiftView? TargetShift, string? ReasonCode, string? Note, DateTime CreatedAt, DateTime? ExpiresAt,
    DateTime? TargetRespondedAt, SwapPersonView? DecidedBy, DateTime? DecidedAt, string? DecisionNote,
    SwapPersonView? CancelledBy, DateTime? CancelledAt, string? CancellationReason,
    string ViewerRole, bool CanCancel, bool CanRespond, bool CanDecide, SwapCheckView[] Checks, DateTime UpdatedAt);

public sealed record SwapCandidateView(SwapPersonView Colleague, SwapShiftView? Shift, double WeeklyHoursAfter, bool Recommended);
/// <summary>Eligible colleagues only; ineligible ones are counted, not listed.</summary>
public sealed record SwapCandidatesView(Guid ShiftAssignmentId, string Type, DateTime Deadline, SwapCandidateView[] Candidates, int HiddenCount);

public sealed record SwapCreateRequest(Guid RequesterShiftAssignmentId, string Type, Guid? TargetUserId, Guid? TargetShiftAssignmentId, string? ReasonCode, string? Note);
public sealed record SwapRespondRequest(bool Accept);
public sealed record SwapCancelRequest(string? Reason);
public sealed record SwapDecisionRequest(string? Note);
public sealed record ManagerHelpRequest(Guid RequesterShiftAssignmentId, string? ReasonCode, string? Note);
public sealed record ManagerAssignCoverRequest(Guid TargetUserId, string? Note);
public sealed record ManagerDirectChangeRequest(string Type, Guid RequesterShiftAssignmentId, Guid TargetUserId, Guid? TargetShiftAssignmentId, string? Note);
