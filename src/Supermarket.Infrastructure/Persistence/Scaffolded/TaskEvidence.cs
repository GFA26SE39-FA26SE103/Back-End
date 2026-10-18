using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class TaskEvidence
{
    public Guid EvidenceId { get; set; }

    public Guid TaskId { get; set; }

    public Guid SubmittedByUserId { get; set; }

    public string EvidenceType { get; set; } = null!;

    public string? EvidenceImage { get; set; }

    public string? Note { get; set; }

    public string ReviewStatus { get; set; } = null!;

    public string? RejectReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual UserAccount SubmittedByUser { get; set; } = null!;

    public virtual Task Task { get; set; } = null!;
}
