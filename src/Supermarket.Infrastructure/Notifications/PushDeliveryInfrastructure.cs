using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Infrastructure.Persistence.Scaffolded;
using Task = System.Threading.Tasks.Task;

namespace Supermarket.Infrastructure.Notifications;

public sealed class FirebaseOptions
{
    public bool Enabled { get; set; }
    public string ProjectId { get; set; } = "";
}

public sealed class FirebasePushSender : IPushSender
{
    private readonly FirebaseMessaging? messaging;
    private FirebasePushSender(FirebaseMessaging? messaging) => this.messaging = messaging;
    public bool Configured => messaging is not null;

    public static FirebasePushSender Create(FirebaseOptions options, ILogger<FirebasePushSender> log)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ProjectId)) return new(null);
        try
        {
            var app = FirebaseApp.Create(new AppOptions
            {
                Credential = GoogleCredential.GetApplicationDefault(),
                ProjectId = options.ProjectId.Trim()
            }, $"fa26se103-push-{Guid.NewGuid():N}");
            return new(FirebaseMessaging.GetMessaging(app));
        }
        catch (Exception error)
        {
            log.LogWarning("Firebase push is unavailable ({ErrorType}).", error.GetType().Name);
            return new(null);
        }
    }

    public async Task<IReadOnlyList<PushSendResult>> Send(PushMessage message, IReadOnlyList<PushDevice> devices, CancellationToken ct)
    {
        if (messaging is null) throw new InvalidOperationException("Firebase push is not configured.");
        var expires = DateTimeOffset.UtcNow.Add(message.TimeToLive).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var messages = devices.Select(device => new Message
        {
            Fid = device.Token,
            Notification = new FirebaseAdmin.Messaging.Notification { Title = message.Title, Body = message.Body },
            Data = new Dictionary<string, string>(message.Data, StringComparer.Ordinal),
            Android = new AndroidConfig
            {
                Priority = Priority.High,
                TimeToLive = message.TimeToLive,
                Notification = new AndroidNotification { ChannelId = message.Channel, ClickAction = "FLUTTER_NOTIFICATION_CLICK" }
            },
            Apns = new ApnsConfig
            {
                Headers = new Dictionary<string, string> { ["apns-expiration"] = expires },
                Aps = new Aps { Sound = "default" }
            }
        }).ToArray();
        var response = await messaging.SendEachAsync(messages, false, ct);
        return response.Responses.Select((value, index) => new PushSendResult(devices[index].DeviceId, Outcome(value))).ToArray();
    }


    private static PushSendOutcome Outcome(SendResponse response)
    {
        if (response.IsSuccess) return PushSendOutcome.Delivered;
        if (response.Exception is not FirebaseMessagingException error) return PushSendOutcome.TransientFailure;
        return error.MessagingErrorCode switch
        {
            MessagingErrorCode.Unregistered or MessagingErrorCode.SenderIdMismatch => PushSendOutcome.InvalidToken,
            MessagingErrorCode.Internal or MessagingErrorCode.QuotaExceeded or MessagingErrorCode.Unavailable => PushSendOutcome.TransientFailure,
            _ => PushSendOutcome.PermanentFailure
        };
    }
}

public sealed class EfPushDispatchStore(AppDbContext db, IOptions<PushDeliveryOptions> configured) : IPushDispatchStore
{
    private const int FetchMultiplier = 4;

    public async Task<PushPolicy> LoadPolicy(CancellationToken ct)
    {
        var keys = NotificationSettingKeys.All;
        var rows = await db.SystemSettings.AsNoTracking().Where(x => keys.Contains(x.SettingKey)).ToListAsync(ct);
        var values = rows.ToDictionary(x => x.SettingKey, x => x.SettingValue, StringComparer.Ordinal);
        return new(Read(values, NotificationSettingKeys.PushEnabled, false),
            Read(values, NotificationSettingKeys.TaskOfferPushEnabled, true),
            Read(values, NotificationSettingKeys.TaskUpdatePushEnabled, true),
            Read(values, NotificationSettingKeys.ShiftSwapPushEnabled, true));
    }

    public async Task<IReadOnlyList<PushEnvelope>> ClaimBatch(int limit, DateTime now, int maxAttempts, CancellationToken ct)
    {
        limit = Math.Clamp(limit, 1, 200);
        maxAttempts = Math.Clamp(maxAttempts, 1, 10);
        var options = configured.Value;
        var retryBase = Math.Clamp(options.PollSeconds * 2, 2, 300);
        var staleBefore = now.AddMinutes(-5);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = N'FA26SE103_FCM_DISPATCH_CLAIM',
                @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;
            IF @result < 0 THROW 51520, 'FCM dispatch claim is busy.', 1;
            """, ct);

        var claimed = new List<NotificationRecipient>();
        claimed.AddRange(await Candidates(x => x.ProviderStatus == null && x.AttemptCount < maxAttempts, limit, ct));
        if (claimed.Count < limit)
            claimed.AddRange(await Candidates(x => x.ProviderStatus == PushProviderStatus.Sending && x.AttemptCount < maxAttempts && x.LastAttemptAt <= staleBefore,
                limit - claimed.Count, ct));
        if (claimed.Count < limit)
        {
            var retryBefore = now.AddSeconds(-retryBase);
            var retry = await Candidates(x => x.ProviderStatus == PushProviderStatus.Retry && x.AttemptCount < maxAttempts && x.LastAttemptAt <= retryBefore,
                Math.Min((limit - claimed.Count) * FetchMultiplier, 800), ct);
            claimed.AddRange(retry.Where(x => x.LastAttemptAt <= now.AddSeconds(-RetryDelay(retryBase, x.AttemptCount))).Take(limit - claimed.Count));
        }

        foreach (var row in claimed)
        {
            row.AttemptCount++;
            row.LastAttemptAt = now;
            row.ProviderStatus = PushProviderStatus.Sending;
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return claimed.Select(row => new PushEnvelope(row.NotificationRecipientId, row.AttemptCount, row.NotificationId, row.UserId,
            row.Notification.NotificationType, row.Notification.Title, row.Notification.Message, row.Notification.Severity,
            Utc(row.Notification.CreatedAt), row.Notification.TaskId, row.Notification.IncidentId, row.Notification.EventId, row.Notification.CameraId)).ToArray();
    }

    public async Task<IReadOnlyList<PushDevice>> ActiveDevices(Guid userId, int limit, CancellationToken ct)
        => await db.UserDevices.AsNoTracking().Where(x => x.UserId == userId && x.RevokedAt == null)
            .OrderByDescending(x => x.LastSeenAt).ThenBy(x => x.DeviceId).Take(Math.Clamp(limit, 1, 50))
            .Select(x => new PushDevice(x.DeviceId, x.FcmToken, x.Platform)).ToListAsync(ct);

    public async Task Complete(Guid recipientId, PushCompletion completion, CancellationToken ct)
    {
        var row = await db.NotificationRecipients.SingleOrDefaultAsync(x => x.NotificationRecipientId == recipientId, ct);
        if (row is null || row.ProviderStatus != PushProviderStatus.Sending) return;
        row.ProviderStatus = completion.Status == PushProviderStatus.Retry || string.IsNullOrWhiteSpace(completion.Detail)
            ? completion.Status : Cut($"{completion.Status};{completion.Detail}", 200);
        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeDevices(IReadOnlyCollection<Guid> deviceIds, DateTime revokedAt, CancellationToken ct)
    {
        if (deviceIds.Count == 0) return;
        var rows = await db.UserDevices.Where(x => deviceIds.Contains(x.DeviceId) && x.RevokedAt == null).ToListAsync(ct);
        foreach (var row in rows) row.RevokedAt = revokedAt;
        await db.SaveChangesAsync(ct);
    }

    private Task<List<NotificationRecipient>> Candidates(System.Linq.Expressions.Expression<Func<NotificationRecipient, bool>> filter,
        int limit, CancellationToken ct) => db.NotificationRecipients.Include(x => x.Notification).Where(x => x.DeliveryChannel == "IN_APP")
        .Where(filter).OrderBy(x => x.Notification.CreatedAt).ThenBy(x => x.NotificationRecipientId).Take(limit).ToListAsync(ct);

    private static bool Read(IReadOnlyDictionary<string, string> values, string key, bool fallback)
        => values.TryGetValue(key, out var raw) && bool.TryParse(raw, out var value) ? value : fallback;
    private static double RetryDelay(int baseSeconds, int attempt) => Math.Min(baseSeconds * Math.Pow(2, Math.Max(0, attempt - 1)), 3600);
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}

public sealed class PushDeliveryWorker(IServiceScopeFactory scopes, IOptions<PushDeliveryOptions> configured, ILogger<PushDeliveryWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configured.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(configured.Value.PollSeconds, 1, 300)));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<PushDispatch>().Tick(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { log.LogWarning("Push dispatch failed ({ErrorType}).", error.GetType().Name); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
