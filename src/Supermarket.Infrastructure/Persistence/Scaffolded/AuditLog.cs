using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class AuditLog
{
    public Guid AuditLogId { get; set; }

    public Guid? UserId { get; set; }

    public string Action { get; set; } = null!;

    public string EntityType { get; set; } = null!;

    public Guid? EntityId { get; set; }

    public string? OldValuesJson { get; set; }

    public string? NewValuesJson { get; set; }

    public string? Reason { get; set; }

    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? ResultStatus { get; set; }

    public virtual UserAccount? User { get; set; }
}
