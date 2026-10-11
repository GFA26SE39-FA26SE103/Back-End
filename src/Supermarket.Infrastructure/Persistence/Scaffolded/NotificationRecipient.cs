using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class NotificationRecipient
{
    public Guid NotificationRecipientId { get; set; }

    public Guid NotificationId { get; set; }

    public Guid UserId { get; set; }

    public string DeliveryChannel { get; set; } = null!;

    public string DeliveryStatus { get; set; } = null!;

    public string? RecipientReason { get; set; }

    public DateTime? DeliveredAt { get; set; }

    public DateTime? ReadAt { get; set; }

    public virtual Notification Notification { get; set; } = null!;

    public virtual UserAccount User { get; set; } = null!;
}
