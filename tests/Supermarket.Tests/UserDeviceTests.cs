using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Supermarket.Api;
using Supermarket.Api.Controllers;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.Persistence;
using Xunit;

namespace Supermarket.Tests;

public sealed class UserDeviceTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);
    private const string TokenA = "fcm-token-a-abcdefghijklmnopqrstuvwxyz-0123456789";
    private const string TokenB = "fcm-token-b-abcdefghijklmnopqrstuvwxyz-0123456789";

    [Fact]
    public async Task StaffRegistersAndRefreshesTheSameTokenIdempotently()
    {
        var world = new World("STAFF");

        var first = await world.Devices.Register(new(TokenA, "android", "1.0.0", null), default);
        world.Clock.UtcNow = Now.AddMinutes(5);
        var second = await world.Devices.Register(new(TokenA, "ANDROID", "1.0.1", null), default);

        Assert.Equal(first.DeviceId, second.DeviceId);
        Assert.Equal("ANDROID", second.Platform);
        Assert.Equal("1.0.1", second.AppVersion);
        Assert.Equal(world.Clock.UtcNow, second.LastSeenAt);
        Assert.Single(world.Store.Values.OfType<UserDevice>());
        Assert.DoesNotContain(TokenA, second.ToString());
    }

    [Fact]
    public async Task TokenRotationUpdatesTheOwnedDeviceWithoutLeavingTheOldTokenActive()
    {
        var world = new World("STAFF");
        var registered = await world.Devices.Register(new(TokenA, "ANDROID", null, null), default);

        var rotated = await world.Devices.Register(new(TokenB, "ANDROID", "2.0", registered.DeviceId), default);

        Assert.Equal(registered.DeviceId, rotated.DeviceId);
        var row = Assert.Single(world.Store.Values.OfType<UserDevice>());
        Assert.Equal(TokenB, row.FcmToken);
        Assert.Null(row.RevokedAt);
    }

    [Fact]
    public async Task ATokenMovingToAnotherStaffAccountHasOnlyOneActiveOwner()
    {
        var store = new MonitoringSetupTests.MemoryStore();
        var firstUser = new Current(Guid.NewGuid(), "STAFF");
        var secondUser = new Current(Guid.NewGuid(), "STAFF");
        var clock = new Clock(Now);
        var first = new UserDevices(store, firstUser, clock);
        var second = new UserDevices(store, secondUser, clock);
        var old = await first.Register(new(TokenA, "ANDROID", null, null), default);

        var transferred = await second.Register(new(TokenA, "ANDROID", null, null), default);

        Assert.Equal(old.DeviceId, transferred.DeviceId);
        var row = Assert.Single(store.Values.OfType<UserDevice>());
        Assert.Equal(secondUser.UserId, row.UserId);
        Assert.Null(row.RevokedAt);
    }

    [Fact]
    public async Task StaffCanRevokeOnlyTheirOwnDeviceAndRepeatSafely()
    {
        var world = new World("STAFF");
        var own = await world.Devices.Register(new(TokenA, "ANDROID", null, null), default);
        var foreign = new UserDevice { DeviceId = Guid.NewGuid(), UserId = Guid.NewGuid(), FcmToken = TokenB, Platform = "ANDROID", CreatedAt = Now };
        world.Store.Values.Add(foreign);

        await world.Devices.Revoke(own.DeviceId, default);
        await world.Devices.Revoke(own.DeviceId, default);
        var error = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() => world.Devices.Revoke(foreign.DeviceId, default));

        Assert.Equal(Now, world.Store.Values.OfType<UserDevice>().Single(x => x.DeviceId == own.DeviceId).RevokedAt);
        Assert.Equal("NOT_FOUND", error.Code);
        Assert.Null(foreign.RevokedAt);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("OPERATOR")]
    [InlineData("MANAGER")]
    public async Task NonStaffCannotRegisterDevices(string role)
    {
        var world = new World(role);

        var error = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() =>
            world.Devices.Register(new(TokenA, "ANDROID", null, null), default));

        Assert.Equal("FORBIDDEN", error.Code);
        Assert.Empty(world.Store.Values);
    }

    [Theory]
    [InlineData("", "ANDROID")]
    [InlineData("token", "WEB")]
    public async Task InvalidRegistrationIsRejected(string token, string platform)
    {
        var world = new World("STAFF");

        var error = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() =>
            world.Devices.Register(new(token, platform, null, null), default));

        Assert.Equal("INVALID_DEVICE", error.Code);
    }

    [Fact]
    public async Task DeviceRoundTripsThroughTheV6Mapper()
    {
        using var db = new Supermarket.Infrastructure.Persistence.Scaffolded.AppDbContext(
            new DbContextOptionsBuilder<Supermarket.Infrastructure.Persistence.Scaffolded.AppDbContext>()
                .UseSqlServer("Server=localhost;Database=mapper-only;Integrated Security=true;TrustServerCertificate=true").Options);
        var store = new EfSetupStore(db, new Clock(Now));
        var device = new UserDevice { DeviceId = Guid.NewGuid(), UserId = Guid.NewGuid(), FcmToken = TokenA, Platform = "ANDROID", AppVersion = "1.0", CreatedAt = Now, LastSeenAt = Now };

        await store.Add(device);
        var loaded = Assert.IsType<UserDevice>(await store.Find<UserDevice>(device.DeviceId));

        Assert.Equal((device.UserId, device.FcmToken, device.Platform), (loaded.UserId, loaded.FcmToken, loaded.Platform));
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAt.Kind);
    }

    [Fact]
    public void DeviceControllerIsStaffOnly()
    {
        Assert.Equal(AccessRoles.Staff, Assert.Single(typeof(MeDevicesController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
    }

    private sealed class World(string role)
    {
        public MonitoringSetupTests.MemoryStore Store { get; } = new();
        public Current User { get; } = new(Guid.NewGuid(), role);
        public Clock Clock { get; } = new(Now);
        public UserDevices Devices => new(Store, User, Clock);
    }

    private sealed record Current(Guid UserId, string Role) : ICurrentUser;
    private sealed class Clock(DateTime now) : IClock { public DateTime UtcNow { get; set; } = now; }
}
