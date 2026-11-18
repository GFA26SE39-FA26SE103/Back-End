using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;
using Supermarket.Domain;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = AccessRoles.Viewer), Route("api/zones/{zoneId:guid}/checkout-runtime")]
public sealed class CheckoutRuntimeController(ISetupStore store, ICurrentUser user, ICheckoutRuntimeState runtime, IClock clock) : ControllerBase
{
    [HttpGet, ResponseCache(NoStore = true)]
    public async Task<CheckoutZoneRuntimeView> Get(Guid zoneId, CancellationToken ct)
    {
        UseCase.Viewer(user);
        UseCase.Found(await store.Find<Zone>(zoneId, ct));
        return runtime.Get(zoneId) ?? new(zoneId, Guid.Empty, default, clock.UtcNow, "NOT_ACTIVE", "CHECKOUT_NOT_ACTIVE",
            null, null, null, false, [], []);
    }
}
