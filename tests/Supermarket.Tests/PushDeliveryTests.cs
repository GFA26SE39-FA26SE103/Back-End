using Supermarket.Application;
using Xunit;

namespace Supermarket.Tests;

public sealed class PushDeliveryTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 16, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task DisabledMasterSkipsWithoutCallingProvider()
    {
        var world = new World(new PushPolicy(false, true, true, true));
        world.Queue("TASK_OFFER");

        var processed = await world.Dispatch.Tick(default);

        Assert.Equal(1, processed);
        Assert.Empty(world.Sender.Messages);
        Assert.Equal(PushProviderStatus.SkippedDisabled, Assert.Single(world.Store.Completions).Status);
    }

    [Fact]
    public async Task SendsStablePayloadToEveryActiveDeviceAndRevokesInvalidTokens()
    {
        var world = new World(new PushPolicy(true, true, true, true));
        var item = world.Queue("TASK_OFFER", taskId: Guid.NewGuid());
        var valid = world.Device("token-valid");
        var invalid = world.Device("token-invalid");
        world.Sender.Results =
        [
            new(valid.DeviceId, PushSendOutcome.Delivered),
            new(invalid.DeviceId, PushSendOutcome.InvalidToken)
        ];

        await world.Dispatch.Tick(default);

        var message = Assert.Single(world.Sender.Messages);
        Assert.Equal(item.NotificationId.ToString(), message.Data["notificationId"]);
        Assert.Equal(item.NotificationType, message.Data["type"]);
        Assert.Equal(item.TaskId.ToString(), message.Data["taskId"]);
        Assert.Equal("/alerts", message.Route);
        Assert.Equal("task_offers", message.Channel);
        Assert.Equal(invalid.DeviceId, Assert.Single(world.Store.Revoked));
        Assert.Equal(PushProviderStatus.Sent, Assert.Single(world.Store.Completions).Status);
    }

    [Fact]
    public async Task TransientFailureRetriesWithinBoundThenBecomesFailed()
    {
        var world = new World(new PushPolicy(true, true, true, true), maxAttempts: 3);
        world.Queue("TASK_ASSIGNED", attempt: 2);
        var device = world.Device("token-transient");
        world.Sender.Results = [new(device.DeviceId, PushSendOutcome.TransientFailure)];

        await world.Dispatch.Tick(default);

        Assert.Equal(PushProviderStatus.Retry, Assert.Single(world.Store.Completions).Status);

        world.Store.Completions.Clear();
        world.Queue("TASK_ASSIGNED", attempt: 3);
        await world.Dispatch.Tick(default);

        Assert.Equal(PushProviderStatus.Failed, Assert.Single(world.Store.Completions).Status);
    }

    [Fact]
    public async Task StaleTaskOfferIsNotSent()
    {
        var world = new World(new PushPolicy(true, true, true, true), taskOfferTtlSeconds: 60);
        world.Queue("TASK_OFFER", createdAt: Now.AddSeconds(-61));
        world.Device("token-valid");

        await world.Dispatch.Tick(default);

        Assert.Empty(world.Sender.Messages);
        Assert.Equal(PushProviderStatus.SkippedExpired, Assert.Single(world.Store.Completions).Status);
    }

    private sealed class World
    {
        public World(PushPolicy policy, int maxAttempts = 4, int taskOfferTtlSeconds = 120)
        {
            Store.Policy = policy;
            Dispatch = new PushDispatch(Store, Sender, new Clock(Now), new PushDeliveryOptions
            {
                BatchSize = 50,
                MaxAttempts = maxAttempts,
                MaxDevicesPerRecipient = 10,
                TaskOfferTtlSeconds = taskOfferTtlSeconds,
                DefaultTtlHours = 24
            });
        }

        public MemoryPushStore Store { get; } = new();
        public MemoryPushSender Sender { get; } = new();
        public PushDispatch Dispatch { get; }

        public PushEnvelope Queue(string type, int attempt = 1, DateTime? createdAt = null, Guid? taskId = null)
        {
            var item = new PushEnvelope(Guid.NewGuid(), attempt, Guid.NewGuid(), Guid.NewGuid(), type, "Title", "Body",
                "WARNING", createdAt ?? Now, taskId, null, null, null);
            Store.Pending.Enqueue(item);
            return item;
        }

        public PushDevice Device(string token)
        {
            var device = new PushDevice(Guid.NewGuid(), token, "ANDROID");
            Store.Devices.Add(device);
            return device;
        }
    }

    private sealed class MemoryPushStore : IPushDispatchStore
    {
        public PushPolicy Policy { get; set; } = new(false, true, true, true);
        public Queue<PushEnvelope> Pending { get; } = new();
        public List<PushDevice> Devices { get; } = [];
        public List<PushCompletion> Completions { get; } = [];
        public List<Guid> Revoked { get; } = [];

        public Task<PushPolicy> LoadPolicy(CancellationToken ct) => Task.FromResult(Policy);
        public Task<IReadOnlyList<PushEnvelope>> ClaimBatch(int limit, DateTime now, int maxAttempts, CancellationToken ct)
        {
            var values = new List<PushEnvelope>();
            while (values.Count < limit && Pending.TryDequeue(out var value)) values.Add(value);
            return Task.FromResult<IReadOnlyList<PushEnvelope>>(values);
        }
        public Task<IReadOnlyList<PushDevice>> ActiveDevices(Guid userId, int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PushDevice>>(Devices.Take(limit).ToArray());
        public Task Complete(Guid recipientId, PushCompletion completion, CancellationToken ct)
        {
            Completions.Add(completion);
            return Task.CompletedTask;
        }
        public Task RevokeDevices(IReadOnlyCollection<Guid> deviceIds, DateTime revokedAt, CancellationToken ct)
        {
            Revoked.AddRange(deviceIds);
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryPushSender : IPushSender
    {
        public bool Configured { get; set; } = true;
        public List<PushMessage> Messages { get; } = [];
        public IReadOnlyList<PushSendResult> Results { get; set; } = [];
        public Task<IReadOnlyList<PushSendResult>> Send(PushMessage message, IReadOnlyList<PushDevice> devices, CancellationToken ct)
        {
            Messages.Add(message);
            return Task.FromResult(Results.Count == 0
                ? (IReadOnlyList<PushSendResult>)devices.Select(x => new PushSendResult(x.DeviceId, PushSendOutcome.Delivered)).ToArray()
                : Results);
        }
    }

    private sealed record Clock(DateTime UtcNow) : IClock;
}
