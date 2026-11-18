using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using Supermarket.Api;
using Supermarket.Api.Controllers;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.Persistence;
using Xunit;

namespace Supermarket.Tests;

public sealed class NotificationSettingsTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task MissingRowsReturnSafeEffectiveDefaults()
    {
        var world = new World("ADMIN");

        var result = await world.Settings.Get(default);

        Assert.False(result.PushEnabled);
        Assert.True(result.TaskOfferPushEnabled);
        Assert.True(result.TaskUpdatePushEnabled);
        Assert.True(result.ShiftSwapPushEnabled);
        Assert.False(result.PushConfigured);
        Assert.Null(result.UpdatedAt);
    }

    [Fact]
    public async Task AdminUpdatePersistsAllKeysAndOneSanitizedAuditRecord()
    {
        var world = new World("ADMIN", pushConfigured: true);

        var result = await world.Settings.Update(new(true, false, true, false, null), default);

        Assert.True(result.PushEnabled);
        Assert.False(result.TaskOfferPushEnabled);
        Assert.True(result.TaskUpdatePushEnabled);
        Assert.False(result.ShiftSwapPushEnabled);
        Assert.Equal(Now, result.UpdatedAt);

        var rows = world.Store.Values.OfType<SystemSetting>().OrderBy(x => x.SettingKey).ToArray();
        Assert.Equal(4, rows.Length);
        Assert.All(rows, row =>
        {
            Assert.Equal("BOOLEAN", row.ValueType);
            Assert.Equal(world.User.UserId, row.UpdatedByUserId);
            Assert.NotEqual(default, row.UpdatedAt);
        });
        Assert.Equal("true", rows.Single(x => x.SettingKey == NotificationSettingKeys.PushEnabled).SettingValue);
        Assert.Equal("false", rows.Single(x => x.SettingKey == NotificationSettingKeys.TaskOfferPushEnabled).SettingValue);

        var audit = Assert.Single(world.Store.Values.OfType<AuditLog>());
        Assert.Equal("UPDATE_NOTIFICATION_SETTINGS", audit.Action);
        Assert.Equal("SystemSetting", audit.EntityType);
        Assert.Equal(world.User.UserId, audit.UserId);
        Assert.Equal("SUCCESS", audit.ResultStatus);
        Assert.DoesNotContain("token", audit.NewValuesJson ?? "", StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(audit.NewValuesJson!);
        Assert.False(json.RootElement.GetProperty("taskOfferPushEnabled").GetBoolean());
    }

    [Fact]
    public async Task NonAdminCannotReadOrUpdateGlobalSettings()
    {
        var world = new World("OPERATOR");

        var read = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() => world.Settings.Get(default));
        var write = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() =>
            world.Settings.Update(new(false, true, true, true, null), default));

        Assert.Equal("FORBIDDEN", read.Code);
        Assert.Equal("FORBIDDEN", write.Code);
        Assert.Empty(world.Store.Values);
    }

    [Fact]
    public async Task PushCannotBeEnabledBeforeTheProviderIsConfigured()
    {
        var world = new World("ADMIN");

        var error = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() =>
            world.Settings.Update(new(true, true, true, true, null), default));

        Assert.Equal("PUSH_NOT_CONFIGURED", error.Code);
        Assert.Equal(422, error.Status);
        Assert.Empty(world.Store.Values);
    }

    [Fact]
    public async Task StaleUpdateIsRejectedWithoutPartialWrites()
    {
        var world = new World("ADMIN", pushConfigured: true);
        world.Seed(NotificationSettingKeys.PushEnabled, "false", Now.AddMinutes(-1));

        var error = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() =>
            world.Settings.Update(new(true, true, true, true, Now.AddMinutes(-2)), default));

        Assert.Equal("SETTINGS_CHANGED", error.Code);
        Assert.Equal("false", world.Store.Values.OfType<SystemSetting>().Single().SettingValue);
        Assert.Empty(world.Store.Values.OfType<AuditLog>());
    }

    [Fact]
    public async Task NotificationAuditRoundTripsThroughTheV6Mapper()
    {
        using var db = new Supermarket.Infrastructure.Persistence.Scaffolded.AppDbContext(
            new DbContextOptionsBuilder<Supermarket.Infrastructure.Persistence.Scaffolded.AppDbContext>()
            .UseSqlServer("Server=localhost;Database=mapper-only;Integrated Security=true;TrustServerCertificate=true").Options);
        var store = new EfSetupStore(db, new Clock(Now));
        var audit = new AuditLog
        {
            AuditLogId = Guid.NewGuid(), UserId = Guid.NewGuid(), Action = "UPDATE_NOTIFICATION_SETTINGS",
            EntityType = "SystemSetting", OldValuesJson = "{}", NewValuesJson = "{\"pushEnabled\":true}",
            CreatedAt = Now, ResultStatus = "SUCCESS"
        };

        await store.Add(audit);
        var row = Assert.Single(db.AuditLogs.Local);
        Assert.Equal(audit.NewValuesJson, row.NewValuesJson);
        var loaded = Assert.IsType<AuditLog>(await store.Find<AuditLog>(audit.AuditLogId));
        Assert.Equal((audit.Action, audit.EntityType, audit.UserId), (loaded.Action, loaded.EntityType, loaded.UserId));
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAt.Kind);
    }

    [Fact]
    public void HttpControllersKeepAdminMutationAndRecipientFeedRolesSeparate()
    {
        Assert.Equal(AccessRoles.Admin, Assert.Single(typeof(NotificationSettingsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        Assert.Equal(AccessRoles.NotificationRecipient, Assert.Single(typeof(MeNotificationsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
    }

    private sealed class World(string role, bool pushConfigured = false)
    {
        public MonitoringSetupTests.MemoryStore Store { get; } = new();
        public Current User { get; } = new(Guid.NewGuid(), role);
        public NotificationSettings Settings => new(Store, User, new Clock(Now), new NotificationCapabilities(pushConfigured));

        public void Seed(string key, string value, DateTime updatedAt) => Store.Values.Add(new SystemSetting
        {
            SettingId = Guid.NewGuid(), SettingKey = key, SettingValue = value, ValueType = "BOOLEAN",
            UpdatedByUserId = User.UserId, UpdatedAt = updatedAt
        });
    }

    private sealed record Current(Guid UserId, string Role) : ICurrentUser;
    private sealed record Clock(DateTime UtcNow) : IClock;
}
