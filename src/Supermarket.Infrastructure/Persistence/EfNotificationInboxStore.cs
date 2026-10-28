using Microsoft.EntityFrameworkCore;
using Supermarket.Application;
using Supermarket.Infrastructure.Persistence.Scaffolded;

namespace Supermarket.Infrastructure.Persistence;

public sealed class EfNotificationInboxStore(AppDbContext db) : INotificationInboxStore
{
    public Task<NotificationView[]> Latest(Guid userId, int limit, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 100);
        return db.NotificationRecipients.AsNoTracking()
            .Where(recipient => recipient.UserId == userId && recipient.DeliveryChannel == "IN_APP")
            .Join(db.Notifications.AsNoTracking(), recipient => recipient.NotificationId, notification => notification.NotificationId,
                (recipient, notification) => new { recipient, notification })
            .OrderByDescending(row => row.notification.CreatedAt)
            .ThenByDescending(row => row.notification.NotificationId)
            .Take(limit)
            .Select(row => new NotificationView(row.notification.NotificationId, row.notification.NotificationType,
                row.notification.Title, row.notification.Message, row.notification.Severity, row.notification.TaskId,
                row.notification.EventId, row.notification.CreatedAt, row.recipient.ReadAt))
            .ToArrayAsync(ct);
    }

    public Task<int> MarkAllRead(Guid userId, DateTime readAt, CancellationToken ct)
        => db.NotificationRecipients
            .Where(recipient => recipient.UserId == userId && recipient.DeliveryChannel == "IN_APP" && recipient.ReadAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(recipient => recipient.ReadAt, readAt), ct);
}
