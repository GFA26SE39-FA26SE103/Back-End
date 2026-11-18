using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class TaskOffer
{
    public Guid OfferId { get; set; }

    public Guid TaskId { get; set; }

    public Guid StaffUserId { get; set; }

    public Guid DispatchCycleId { get; set; }

    public int WaveNumber { get; set; }

    public string DispatchScope { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? DeclineReasonCode { get; set; }

    public DateTime OfferedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? RespondedAt { get; set; }

    public virtual UserAccount StaffUser { get; set; } = null!;

    public virtual Task Task { get; set; } = null!;
}
