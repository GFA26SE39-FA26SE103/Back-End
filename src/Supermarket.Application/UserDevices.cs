using System.ComponentModel.DataAnnotations;
using Supermarket.Domain;

namespace Supermarket.Application;

public sealed record RegisterDeviceRequest([property: Required, MaxLength(512)] string FcmToken,
    [property: Required, MaxLength(40)] string Platform, [property: MaxLength(100)] string? AppVersion, Guid? DeviceId);
public sealed record UserDeviceView(Guid DeviceId, string Platform, string? AppVersion, DateTime CreatedAt,
    DateTime? LastSeenAt, DateTime? RevokedAt);

/// <summary>Staff-owned mobile device lifecycle. FCM tokens are accepted for persistence but never returned.</summary>
public sealed class UserDevices(ISetupStore store, ICurrentUser current, IClock clock)
{
    public async Task<UserDeviceView> Register(RegisterDeviceRequest request, CancellationToken ct)
    {
        UseCase.Staff(current);
        var token = request.FcmToken?.Trim() ?? "";
        var platform = request.Platform?.Trim().ToUpperInvariant() ?? "";
        var appVersion = string.IsNullOrWhiteSpace(request.AppVersion) ? null : request.AppVersion.Trim();
        if (token.Length is < 20 or > 512 || platform is not ("ANDROID" or "IOS") || appVersion?.Length > 100)
            throw new ApplicationException("INVALID_DEVICE", "The device registration is invalid.", 422);

        return await store.Transaction(async () =>
        {
            var now = clock.UtcNow;
            UserDevice? requestedDevice = null;
            if (request.DeviceId is { } deviceId)
            {
                requestedDevice = await store.Find<UserDevice>(deviceId, ct);
                if (requestedDevice is null || requestedDevice.UserId != current.UserId)
                    throw new ApplicationException("NOT_FOUND", "The resource was not found.", 404);
            }

            var tokenOwner = (await store.List<UserDevice>(x => x.FcmToken == token, ct)).SingleOrDefault();
            UserDevice device;
            if (tokenOwner is not null)
            {
                device = tokenOwner;
                if (requestedDevice is not null && requestedDevice.DeviceId != device.DeviceId && requestedDevice.RevokedAt is null)
                {
                    requestedDevice.RevokedAt = now;
                    await store.Update(requestedDevice, ct);
                }
            }
            else if (requestedDevice is not null)
            {
                device = requestedDevice;
                device.FcmToken = token;
            }
            else
            {
                device = new UserDevice { DeviceId = Guid.NewGuid(), FcmToken = token, CreatedAt = now };
            }

            device.UserId = current.UserId;
            device.Platform = platform;
            device.AppVersion = appVersion;
            device.LastSeenAt = now;
            device.RevokedAt = null;
            if (tokenOwner is null && requestedDevice is null) await store.Add(device, ct);
            else await store.Update(device, ct);
            return View(device);
        }, ct);
    }

    public async Task Revoke(Guid deviceId, CancellationToken ct)
    {
        UseCase.Staff(current);
        await store.Transaction(async () =>
        {
            var device = await store.Find<UserDevice>(deviceId, ct);
            if (device is null || device.UserId != current.UserId)
                throw new ApplicationException("NOT_FOUND", "The resource was not found.", 404);
            if (device.RevokedAt is null)
            {
                device.RevokedAt = clock.UtcNow;
                await store.Update(device, ct);
            }
            return 0;
        }, ct);
    }

    private static UserDeviceView View(UserDevice value)
        => new(value.DeviceId, value.Platform, value.AppVersion, value.CreatedAt, value.LastSeenAt, value.RevokedAt);
}
