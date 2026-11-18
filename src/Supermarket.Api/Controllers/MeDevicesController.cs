using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = AccessRoles.Staff), Route("api/me/devices")]
public sealed class MeDevicesController(UserDevices devices) : ControllerBase
{
    [HttpPost]
    public Task<UserDeviceView> Register(RegisterDeviceRequest request, CancellationToken ct) => devices.Register(request, ct);

    [HttpDelete("{deviceId:guid}"), ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(Guid deviceId, CancellationToken ct)
    {
        await devices.Revoke(deviceId, ct);
        return NoContent();
    }
}
