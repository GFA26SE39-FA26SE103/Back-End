using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = AccessRoles.Admin), Route("api/admin/settings/notifications")]
public sealed class NotificationSettingsController(NotificationSettings settings) : ControllerBase
{
    [HttpGet]
    public Task<NotificationSettingsView> Get(CancellationToken ct) => settings.Get(ct);

    [HttpPut]
    public Task<NotificationSettingsView> Update(UpdateNotificationSettingsRequest request, CancellationToken ct)
        => settings.Update(request, ct);
}
