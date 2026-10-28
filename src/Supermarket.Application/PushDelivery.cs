namespace Supermarket.Application;

public sealed record PushPolicy(bool PushEnabled, bool TaskOfferEnabled, bool TaskUpdateEnabled, bool ShiftSwapEnabled);

public sealed record PushEnvelope(Guid RecipientId, int AttemptCount, Guid NotificationId, Guid UserId,
    string NotificationType, string Title, string Body, string? Severity, DateTime CreatedAt,
    Guid? TaskId, Guid? IncidentId, Guid? EventId, Guid? CameraId);

public sealed record PushDevice(Guid DeviceId, string Token, string Platform);

public sealed record PushMessage(string Title, string Body, string Channel, string Route,
    IReadOnlyDictionary<string, string> Data, TimeSpan TimeToLive);

public enum PushSendOutcome { Delivered, InvalidToken, TransientFailure, PermanentFailure }
public sealed record PushSendResult(Guid DeviceId, PushSendOutcome Outcome);
public sealed record PushCompletion(string Status, string? Detail = null);

public static class PushProviderStatus
{
    public const string Sending = "FCM_SENDING", Retry = "FCM_RETRY", Sent = "FCM_SENT", Failed = "FCM_FAILED",
        SkippedDisabled = "FCM_SKIPPED_DISABLED", SkippedCategory = "FCM_SKIPPED_CATEGORY",
        SkippedExpired = "FCM_SKIPPED_EXPIRED", SkippedNoDevice = "FCM_SKIPPED_NO_DEVICE",
        SkippedUnsupported = "FCM_SKIPPED_UNSUPPORTED", SkippedNotConfigured = "FCM_SKIPPED_NOT_CONFIGURED";
}

public sealed class PushDeliveryOptions
{
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 2;
    public int BatchSize { get; set; } = 50;
    public int MaxAttempts { get; set; } = 4;
    public int MaxDevicesPerRecipient { get; set; } = 10;
    public int TaskOfferTtlSeconds { get; set; } = 120;
    public int DefaultTtlHours { get; set; } = 24;
}

public interface IPushDispatchStore
{
    Task<PushPolicy> LoadPolicy(CancellationToken ct);
    Task<IReadOnlyList<PushEnvelope>> ClaimBatch(int limit, DateTime now, int maxAttempts, CancellationToken ct);
    Task<IReadOnlyList<PushDevice>> ActiveDevices(Guid userId, int limit, CancellationToken ct);
    Task Complete(Guid recipientId, PushCompletion completion, CancellationToken ct);
    Task RevokeDevices(IReadOnlyCollection<Guid> deviceIds, DateTime revokedAt, CancellationToken ct);
}

public interface IPushSender
{
    bool Configured { get; }
    Task<IReadOnlyList<PushSendResult>> Send(PushMessage message, IReadOnlyList<PushDevice> devices, CancellationToken ct);
}

/// <summary>Processes durable NotificationRecipient rows after their business transaction commits.</summary>
public sealed class PushDispatch(IPushDispatchStore store, IPushSender sender, IClock clock, PushDeliveryOptions options)
{
    public async Task<int> Tick(CancellationToken ct)
    {
        var policy = await store.LoadPolicy(ct);
        var batch = await store.ClaimBatch(Math.Clamp(options.BatchSize, 1, 200), clock.UtcNow,
            Math.Clamp(options.MaxAttempts, 1, 10), ct);
        foreach (var item in batch) await Process(item, policy, ct);
        return batch.Count;
    }

    private async Task Process(PushEnvelope item, PushPolicy policy, CancellationToken ct)
    {
        var category = Category(item.NotificationType);
        if (category is null) { await Done(item, PushProviderStatus.SkippedUnsupported, ct); return; }
        if (!policy.PushEnabled) { await Done(item, PushProviderStatus.SkippedDisabled, ct); return; }
        if (!Enabled(category, policy)) { await Done(item, PushProviderStatus.SkippedCategory, ct); return; }
        if (!sender.Configured) { await Done(item, PushProviderStatus.SkippedNotConfigured, ct); return; }

        var ttl = category == "task_offers"
            ? TimeSpan.FromSeconds(Math.Clamp(options.TaskOfferTtlSeconds, 30, 3600))
            : TimeSpan.FromHours(Math.Clamp(options.DefaultTtlHours, 1, 168));
        if (clock.UtcNow - item.CreatedAt.ToUniversalTime() >= ttl)
        {
            await Done(item, PushProviderStatus.SkippedExpired, ct);
            return;
        }

        var devices = await store.ActiveDevices(item.UserId, Math.Clamp(options.MaxDevicesPerRecipient, 1, 50), ct);
        if (devices.Count == 0) { await Done(item, PushProviderStatus.SkippedNoDevice, ct); return; }

        try
        {
            var results = await sender.Send(Message(item, category, ttl), devices, ct);
            var invalid = results.Where(x => x.Outcome == PushSendOutcome.InvalidToken).Select(x => x.DeviceId).Distinct().ToArray();
            if (invalid.Length > 0) await store.RevokeDevices(invalid, clock.UtcNow, ct);
            if (results.Count != devices.Count || results.Any(x => x.Outcome == PushSendOutcome.TransientFailure))
            {
                await RetryOrFail(item, "TRANSIENT", ct);
                return;
            }
            var delivered = results.Count(x => x.Outcome == PushSendOutcome.Delivered);
            await store.Complete(item.RecipientId, new(delivered > 0 ? PushProviderStatus.Sent : PushProviderStatus.Failed,
                $"DELIVERED={delivered};INVALID={invalid.Length};PERMANENT={results.Count(x => x.Outcome == PushSendOutcome.PermanentFailure)}"), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { await RetryOrFail(item, "PROVIDER_ERROR", ct); }
    }

    private Task RetryOrFail(PushEnvelope item, string detail, CancellationToken ct)
        => store.Complete(item.RecipientId, new(item.AttemptCount < Math.Clamp(options.MaxAttempts, 1, 10)
            ? PushProviderStatus.Retry : PushProviderStatus.Failed, detail), ct);

    private Task Done(PushEnvelope item, string status, CancellationToken ct)
        => store.Complete(item.RecipientId, new(status), ct);

    private static string? Category(string type) => type switch
    {
        "TASK_OFFER" => "task_offers",
        _ when type.StartsWith("TASK_", StringComparison.Ordinal) || type.StartsWith("EVENT_", StringComparison.Ordinal) => "task_updates",
        _ when type.StartsWith("SHIFT_", StringComparison.Ordinal) || type.StartsWith("SWAP_", StringComparison.Ordinal) => "shifts_swaps",
        _ => null
    };

    private static bool Enabled(string category, PushPolicy policy) => category switch
    {
        "task_offers" => policy.TaskOfferEnabled,
        "task_updates" => policy.TaskUpdateEnabled,
        "shifts_swaps" => policy.ShiftSwapEnabled,
        _ => false
    };

    private static PushMessage Message(PushEnvelope item, string channel, TimeSpan ttl)
    {
        var route = item.NotificationType == "TASK_OFFER" ? "/alerts"
            : item.TaskId is { } taskId ? $"/tasks/{taskId}"
            : item.NotificationType.StartsWith("SWAP_", StringComparison.Ordinal) ? "/shifts/swaps"
            : item.NotificationType.StartsWith("SHIFT_", StringComparison.Ordinal) ? "/shifts" : "/alerts";
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["notificationId"] = item.NotificationId.ToString(), ["type"] = item.NotificationType, ["route"] = route
        };
        Add(data, "taskId", item.TaskId); Add(data, "incidentId", item.IncidentId);
        Add(data, "eventId", item.EventId); Add(data, "cameraId", item.CameraId);
        if (!string.IsNullOrWhiteSpace(item.Severity)) data["severity"] = item.Severity;
        return new(Cut(item.Title, 200), Cut(item.Body, 500), channel, route, data, ttl);
    }

    private static void Add(IDictionary<string, string> data, string key, Guid? value)
    {
        if (value is { } id) data[key] = id.ToString();
    }
    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}
