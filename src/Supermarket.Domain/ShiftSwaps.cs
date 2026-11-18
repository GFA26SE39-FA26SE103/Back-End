namespace Supermarket.Domain;

/// <summary>dbo.ShiftSwapRequest: Staff A asks Staff B to swap or cover a future shift; B answers, then a Manager decides.</summary>
public sealed class ShiftSwapRequest : TrackedEntity
{
    public Guid SwapRequestId { get; set; }
    public string RequestType { get; set; } = SwapType.Swap;
    public Guid RequestedByUserId { get; set; }
    public Guid RequesterShiftAssignmentId { get; set; }
    public Guid? TargetUserId { get; set; }
    public Guid? TargetShiftAssignmentId { get; set; }
    public string? ReasonCode { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = SwapStatus.PendingPeer;
    public DateTime? TargetRespondedAt { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
}
public sealed class StaffUnavailability : Entity
{
    public Guid UnavailabilityId { get; set; }
    public Guid UserId { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>SWAP exchanges A's and B's shifts; COVER gives A's shift to B.</summary>
public static class SwapType
{
    public const string Swap = "SWAP", Cover = "COVER";
}
/// <summary>A → PENDING_PEER → (B accepts) PENDING_MANAGER → APPROVED / REJECTED; DECLINED_BY_PEER, CANCELLED and EXPIRED end a pending request.</summary>
public static class SwapStatus
{
    public const string PendingPeer = "PENDING_PEER", DeclinedByPeer = "DECLINED_BY_PEER", PendingManager = "PENDING_MANAGER",
        Approved = "APPROVED", Rejected = "REJECTED", Cancelled = "CANCELLED", Expired = "EXPIRED";
    public static readonly string[] Pending = [PendingPeer, PendingManager];
}
public static class SwapReason
{
    public const string Personal = "PERSONAL", Health = "HEALTH", Study = "STUDY", Other = "OTHER";
}
public static class SwapRules
{
    /// <summary>A pending request expires when this much time remains before the earliest affected shift starts.</summary>
    public static readonly TimeSpan DecisionLead = TimeSpan.FromHours(24);
    /// <summary>BR weekly working-hour limit per Staff member.</summary>
    public const double MaxWeeklyHours = 48;
    /// <summary>Planning target that leaves capacity for covers; this is a warning, not a hard rejection.</summary>
    public const double RecommendedWeeklyHours = 40;

    /// <summary>The absolute deadline: 24 hours before the earlier of the two shifts (A's shift for COVER).</summary>
    public static DateTime Deadline(DateTime requesterStartsAt, DateTime? targetStartsAt)
        => (targetStartsAt is { } target && target < requesterStartsAt ? target : requesterStartsAt) - DecisionLead;

    public static void Request(string? type, Guid? targetUserId, Guid? targetShiftAssignmentId, string? reasonCode, string? note)
    {
        Rules.Require(type is SwapType.Swap or SwapType.Cover, "INVALID_SWAP_TYPE", "The request type must be SWAP or COVER.");
        Rules.Require(targetUserId is { } user && user != Guid.Empty, "TARGET_REQUIRED", "Choose the colleague who should take the shift.");
        if (type == SwapType.Swap)
            Rules.Require(targetShiftAssignmentId is { } target && target != Guid.Empty, "TARGET_SHIFT_REQUIRED", "Choose the colleague's shift to swap with.");
        else
            Rules.Require(targetShiftAssignmentId is null, "TARGET_SHIFT_NOT_ALLOWED", "A cover request only moves your own shift.");
        Rules.Require(reasonCode is null or SwapReason.Personal or SwapReason.Health or SwapReason.Study or SwapReason.Other, "INVALID_REASON",
            "The reason must be PERSONAL, HEALTH, STUDY or OTHER.");
        Rules.Optional(note, 1000, "Note");
    }
}
