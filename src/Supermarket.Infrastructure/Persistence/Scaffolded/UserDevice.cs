using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class UserDevice
{
    public Guid DeviceId { get; set; }

    public Guid UserId { get; set; }

    public string FcmToken { get; set; } = null!;

    public string Platform { get; set; } = null!;

    public string? AppVersion { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastSeenAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public virtual UserAccount User { get; set; } = null!;
}
