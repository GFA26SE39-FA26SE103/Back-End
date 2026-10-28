namespace Supermarket.Application;

public interface INotificationInboxStore
{
    Task<NotificationView[]> Latest(Guid userId, int limit, CancellationToken ct);
    Task<int> MarkAllRead(Guid userId, DateTime readAt, CancellationToken ct);
}

/// <summary>Recipient-scoped in-app inbox shared by Staff, Operator and Manager.</summary>
public sealed class NotificationInbox(INotificationInboxStore store, ICurrentUser current, IClock clock)
{
    public Task<NotificationView[]> List(CancellationToken ct)
    {
        RequireRecipientRole();
        return store.Latest(current.UserId, 100, ct);
    }

    public Task<int> ReadAll(CancellationToken ct)
    {
        RequireRecipientRole();
        return store.MarkAllRead(current.UserId, clock.UtcNow, ct);
    }

    private void RequireRecipientRole()
    {
        if (current.Role is not ("STAFF" or "OPERATOR" or "MANAGER"))
            throw new ApplicationException("FORBIDDEN", "Notification inbox access is required.", 403);
    }
}
