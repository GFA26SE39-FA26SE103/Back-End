using System;
using System.Collections.Generic;

namespace Supermarket.Infrastructure.Persistence.Scaffolded;

public partial class Notification
{
    public Guid NotificationId { get; set; }

    public Guid? TaskId { get; set; }

    public Guid? IncidentId { get; set; }

    public Guid? CameraId { get; set; }

    public string NotificationType { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Message { get; set; } = null!;

    public string? Severity { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? EventId { get; set; }

    public virtual Camera? Camera { get; set; }

    public virtual OperationalEvent? Event { get; set; }

    public virtual Incident? Incident { get; set; }

    public virtual ICollection<NotificationRecipient> NotificationRecipients { get; set; } = new List<NotificationRecipient>();

    public virtual Task? Task { get; set; }
}
