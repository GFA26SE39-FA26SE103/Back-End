using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = AccessRoles.NotificationRecipient), Route("api/me/notifications")]
public sealed class MeNotificationsController(NotificationInbox inbox) : ControllerBase
{
    [HttpGet] public Task<NotificationView[]> List(CancellationToken ct) => inbox.List(ct);

    [HttpPost("read-all"), ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        await inbox.ReadAll(ct);
        return NoContent();
    }
}
