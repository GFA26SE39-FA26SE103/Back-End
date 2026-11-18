using Supermarket.Application;
using Supermarket.Domain;
using Xunit;

namespace Supermarket.Tests;

public sealed class NotificationInboxTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("STAFF")]
    [InlineData("OPERATOR")]
    [InlineData("MANAGER")]
    public async Task SupportedRolesReadOnlyTheirOwnRecipientRows(string role)
    {
        var world = new World(role);
        var mine = world.Add("TASK_NEEDS_ASSIGNMENT", world.User.UserId, Now);
        world.Add("SHIFT_PUBLISHED", Guid.NewGuid(), Now.AddMinutes(1));

        var result = await world.Inbox.List(default);

        Assert.Collection(result, item => Assert.Equal(mine, item.NotificationId));
    }

    [Fact]
    public async Task ReadAllMarksOnlyTheCurrentRecipientsWithoutChangingBusinessState()
    {
        var world = new World("OPERATOR");
        var mine = world.Add("TASK_NEEDS_ASSIGNMENT", world.User.UserId, Now);
        var otherUser = Guid.NewGuid();
        var other = world.Add("TASK_ASSIGNED", otherUser, Now);

        var changed = await world.Inbox.ReadAll(default);

        Assert.Equal(1, changed);
        Assert.Equal(Now, world.Recipient(mine).ReadAt);
        Assert.Null(world.Recipient(other).ReadAt);
    }

    [Fact]
    public async Task UnsupportedRoleCannotUseRecipientFeed()
    {
        var world = new World("ADMIN");

        var error = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() => world.Inbox.List(default));

        Assert.Equal("FORBIDDEN", error.Code);
    }

    [Fact]
    public async Task FeedRemainsBoundedToNewestOneHundredRows()
    {
        var world = new World("STAFF");
        for (var i = 0; i < 105; i++) world.Add("TASK_UPDATE", world.User.UserId, Now.AddMinutes(i));

        var result = await world.Inbox.List(default);

        Assert.Equal(100, result.Length);
        Assert.Equal(Now.AddMinutes(104), result[0].CreatedAt);
        Assert.Equal(Now.AddMinutes(5), result[^1].CreatedAt);
    }

    private sealed class World(string role)
    {
        public MonitoringSetupTests.MemoryStore Store { get; } = new();
        public Current User { get; } = new(Guid.NewGuid(), role);
        public NotificationInbox Inbox => new(new StoreAdapter(Store), User, new Clock(Now));

        public Guid Add(string type, Guid recipient, DateTime createdAt)
        {
            var id = Guid.NewGuid();
            Store.Values.Add(new Notification { NotificationId = id, NotificationType = type, Title = type, Message = "Message", CreatedAt = createdAt });
            Store.Values.Add(new NotificationRecipient
            {
                NotificationRecipientId = Guid.NewGuid(), NotificationId = id, UserId = recipient,
                DeliveryChannel = "IN_APP", DeliveryStatus = "SENT", DeliveredAt = createdAt
            });
            return id;
        }

        public NotificationRecipient Recipient(Guid notificationId) =>
            Store.Values.OfType<NotificationRecipient>().Single(x => x.NotificationId == notificationId);
    }

    private sealed record Current(Guid UserId, string Role) : ICurrentUser;
    private sealed record Clock(DateTime UtcNow) : IClock;

    internal sealed class StoreAdapter(MonitoringSetupTests.MemoryStore store) : INotificationInboxStore
    {
        public async Task<NotificationView[]> Latest(Guid userId, int limit, CancellationToken ct)
        {
            var recipients = await store.List<NotificationRecipient>(x => x.UserId == userId && x.DeliveryChannel == "IN_APP", ct);
            var ids = recipients.Select(x => x.NotificationId).ToHashSet();
            var notifications = await store.List<Notification>(x => ids.Contains(x.NotificationId), ct);
            return notifications.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.NotificationId).Take(limit)
                .Select(x => new NotificationView(x.NotificationId, x.NotificationType, x.Title, x.Message, x.Severity,
                    x.TaskId, x.EventId, x.CreatedAt, recipients.Single(r => r.NotificationId == x.NotificationId).ReadAt)).ToArray();
        }

        public async Task<int> MarkAllRead(Guid userId, DateTime readAt, CancellationToken ct)
        {
            var recipients = await store.List<NotificationRecipient>(x => x.UserId == userId && x.DeliveryChannel == "IN_APP" && x.ReadAt == null, ct);
            foreach (var recipient in recipients)
            {
                recipient.ReadAt = readAt;
                await store.Update(recipient, ct);
            }
            return recipients.Count;
        }
    }
}
