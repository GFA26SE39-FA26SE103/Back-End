using System.Text.Json;
using Supermarket.Domain;

namespace Supermarket.Application;

public static class NotificationSettingKeys
{
    public const string PushEnabled = "notification.push_enabled";
    public const string TaskOfferPushEnabled = "notification.task_offer_push_enabled";
    public const string TaskUpdatePushEnabled = "notification.task_update_push_enabled";
    public const string ShiftSwapPushEnabled = "notification.shift_swap_push_enabled";

    public static readonly string[] All = [PushEnabled, TaskOfferPushEnabled, TaskUpdatePushEnabled, ShiftSwapPushEnabled];
}

public sealed record NotificationCapabilities(bool PushConfigured);
public sealed record NotificationSettingsView(bool PushEnabled, bool TaskOfferPushEnabled, bool TaskUpdatePushEnabled,
    bool ShiftSwapPushEnabled, bool PushConfigured, DateTime? UpdatedAt);
public sealed record UpdateNotificationSettingsRequest(bool PushEnabled, bool TaskOfferPushEnabled, bool TaskUpdatePushEnabled,
    bool ShiftSwapPushEnabled, DateTime? ExpectedUpdatedAt);

/// <summary>Admin-managed, non-secret global FCM policy stored in the existing V6 SystemSetting table.</summary>
public sealed class NotificationSettings(ISetupStore store, ICurrentUser current, IClock clock, NotificationCapabilities capabilities)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<NotificationSettingsView> Get(CancellationToken ct)
    {
        UseCase.Admin(current);
        return View(await Rows(ct));
    }

    public async Task<NotificationSettingsView> Update(UpdateNotificationSettingsRequest request, CancellationToken ct)
    {
        UseCase.Admin(current);
        if (request.PushEnabled && !capabilities.PushConfigured)
            throw new ApplicationException("PUSH_NOT_CONFIGURED", "FCM push is not configured on this server.", 422);
        return await store.Transaction(async () =>
        {
            var rows = await Rows(ct);
            var before = View(rows);
            if (Normalize(request.ExpectedUpdatedAt) != Normalize(before.UpdatedAt))
                throw new ApplicationException("SETTINGS_CHANGED", "Notification settings changed; reload before saving.");

            var requested = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                [NotificationSettingKeys.PushEnabled] = request.PushEnabled,
                [NotificationSettingKeys.TaskOfferPushEnabled] = request.TaskOfferPushEnabled,
                [NotificationSettingKeys.TaskUpdatePushEnabled] = request.TaskUpdatePushEnabled,
                [NotificationSettingKeys.ShiftSwapPushEnabled] = request.ShiftSwapPushEnabled
            };
            var now = clock.UtcNow;
            foreach (var (key, value) in requested)
            {
                var row = rows.SingleOrDefault(x => x.SettingKey == key);
                var isNew = row is null;
                if (row is null)
                {
                    row = new SystemSetting { SettingId = Guid.NewGuid(), SettingKey = key };
                    rows.Add(row);
                }
                row.SettingValue = value ? "true" : "false";
                row.ValueType = "BOOLEAN";
                row.UpdatedByUserId = current.UserId;
                row.UpdatedAt = now;
                if (isNew) await store.Add(row, ct);
                else await store.Update(row, ct);
            }

            var after = new NotificationSettingsView(request.PushEnabled, request.TaskOfferPushEnabled, request.TaskUpdatePushEnabled,
                request.ShiftSwapPushEnabled, capabilities.PushConfigured, now);
            await store.Add(new AuditLog
            {
                AuditLogId = Guid.NewGuid(), UserId = current.UserId, Action = "UPDATE_NOTIFICATION_SETTINGS",
                EntityType = "SystemSetting", OldValuesJson = JsonSerializer.Serialize(Policy(before), Json),
                NewValuesJson = JsonSerializer.Serialize(Policy(after), Json), CreatedAt = now, ResultStatus = "SUCCESS"
            }, ct);
            return after;
        }, ct);
    }

    private Task<List<SystemSetting>> Rows(CancellationToken ct)
        => store.List<SystemSetting>(x => NotificationSettingKeys.All.Contains(x.SettingKey), ct);

    private NotificationSettingsView View(IEnumerable<SystemSetting> rows)
    {
        var latest = rows.GroupBy(x => x.SettingKey).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.UpdatedAt).First());
        return new(Read(latest, NotificationSettingKeys.PushEnabled, false),
            Read(latest, NotificationSettingKeys.TaskOfferPushEnabled, true),
            Read(latest, NotificationSettingKeys.TaskUpdatePushEnabled, true),
            Read(latest, NotificationSettingKeys.ShiftSwapPushEnabled, true), capabilities.PushConfigured,
            latest.Count == 0 ? null : latest.Values.Max(x => x.UpdatedAt));
    }

    private static bool Read(IReadOnlyDictionary<string, SystemSetting> rows, string key, bool fallback)
        => rows.TryGetValue(key, out var row) && bool.TryParse(row.SettingValue, out var value) ? value : fallback;
    private static DateTime? Normalize(DateTime? value) => value?.ToUniversalTime();
    private static object Policy(NotificationSettingsView value) => new
    {
        value.PushEnabled, value.TaskOfferPushEnabled, value.TaskUpdatePushEnabled, value.ShiftSwapPushEnabled
    };
}
