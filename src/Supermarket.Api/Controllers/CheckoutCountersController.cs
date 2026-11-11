using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize]
public sealed class CheckoutCountersController(CheckoutConfiguration checkout) : ControllerBase
{
    [Authorize(Roles = AccessRoles.Viewer), HttpGet("/api/zones/{zoneId:guid}/checkout-counters")]
    public Task<List<CheckoutCounterView>> List(Guid zoneId, CancellationToken ct)
        => checkout.CountersByZone(zoneId, ct);

    [Authorize(Roles = AccessRoles.Admin), HttpPost("/api/zones/{zoneId:guid}/checkout-counters")]
    public async Task<IActionResult> Create(Guid zoneId, CheckoutCounterRequest request, CancellationToken ct)
    {
        var counter = await checkout.Create(zoneId, request, ct);
        return Created($"/api/checkout-counters/{counter.CheckoutCounterId}", counter);
    }

    [Authorize(Roles = AccessRoles.Viewer), HttpGet("/api/checkout-counters/{counterId:guid}")]
    public Task<CheckoutCounterView> Get(Guid counterId, CancellationToken ct) => checkout.Get(counterId, ct);

    [Authorize(Roles = AccessRoles.Admin), HttpPatch("/api/checkout-counters/{counterId:guid}")]
    public Task<CheckoutCounterView> Update(Guid counterId, CheckoutCounterRequest request, CancellationToken ct)
        => checkout.Update(counterId, request, ct);

    [Authorize(Roles = AccessRoles.Admin), HttpDelete("/api/checkout-counters/{counterId:guid}")]
    public async Task<IActionResult> Delete(Guid counterId, [FromQuery] DateTime? expectedUpdatedAt, CancellationToken ct)
    {
        await checkout.Delete(counterId, expectedUpdatedAt, ct);
        return NoContent();
    }

    [Authorize(Roles = AccessRoles.Viewer), HttpGet("/api/cameras/{cameraId:guid}/checkout-mappings")]
    public Task<List<CheckoutMappingView>> ByCamera(Guid cameraId, CancellationToken ct)
        => checkout.MappingsByCamera(cameraId, ct);

    [Authorize(Roles = AccessRoles.Viewer), HttpGet("/api/checkout-counters/{counterId:guid}/camera-mappings")]
    public Task<List<CheckoutMappingView>> ByCounter(Guid counterId, CancellationToken ct)
        => checkout.MappingsByCounter(counterId, ct);

    [Authorize(Roles = AccessRoles.Admin), HttpPut("/api/checkout-counters/{counterId:guid}/camera-mappings/{cameraId:guid}")]
    public Task<CheckoutMappingView> PutMapping(Guid counterId, Guid cameraId, CheckoutMappingRequest request, CancellationToken ct)
        => checkout.PutMapping(counterId, cameraId, request, ct);

    [Authorize(Roles = AccessRoles.Admin), HttpDelete("/api/checkout-counters/{counterId:guid}/camera-mappings/{cameraId:guid}")]
    public async Task<IActionResult> DeleteMapping(Guid counterId, Guid cameraId, [FromQuery] DateTime? expectedUpdatedAt, CancellationToken ct)
    {
        await checkout.DeleteMapping(counterId, cameraId, expectedUpdatedAt, ct);
        return NoContent();
    }
}
