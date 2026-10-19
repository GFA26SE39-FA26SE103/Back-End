using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class ShiftSwapRequest
{
    public Guid SwapRequestId { get; set; }

    public string RequestType { get; set; } = null!;

    public Guid RequestedByUserId { get; set; }

    public Guid RequesterShiftAssignmentId { get; set; }

    public Guid? TargetUserId { get; set; }

    public Guid? TargetShiftAssignmentId { get; set; }

    public string? ReasonCode { get; set; }

    public string? Note { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? TargetRespondedAt { get; set; }

    public Guid? DecidedByUserId { get; set; }

    public DateTime? DecidedAt { get; set; }

    public string? DecisionNote { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public Guid? CancelledByUserId { get; set; }

    public DateTime? CancelledAt { get; set; }

    public string? CancellationReason { get; set; }

    public virtual UserAccount? CancelledByUser { get; set; }

    public virtual UserAccount? DecidedByUser { get; set; }

    public virtual UserAccount RequestedByUser { get; set; } = null!;

    public virtual ShiftAssignment RequesterShiftAssignment { get; set; } = null!;

    public virtual ShiftAssignment? TargetShiftAssignment { get; set; }

    public virtual UserAccount? TargetUser { get; set; }
}
