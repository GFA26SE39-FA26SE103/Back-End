using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = AccessRoles.Admin), Route("api/operational-event-types")]
public sealed class OperationalEventTypesController(OperationalEventTypeCatalog catalog) : ControllerBase
{
    [HttpGet] public Task<OperationalEventTypeAdminView[]> List(CancellationToken ct) => catalog.List(ct);
    [HttpGet("{eventTypeId:guid}")] public Task<OperationalEventTypeAdminView> Get(Guid eventTypeId, CancellationToken ct) => catalog.Get(eventTypeId, ct);
    [HttpPost] public Task<OperationalEventTypeAdminView> Create(OperationalEventTypeCreateRequest request, CancellationToken ct) => catalog.Create(request, ct);
    [HttpPut("{eventTypeId:guid}")] public Task<OperationalEventTypeAdminView> Update(Guid eventTypeId, OperationalEventTypeUpdateRequest request, CancellationToken ct) => catalog.Update(eventTypeId, request, ct);
    [HttpDelete("{eventTypeId:guid}"), ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid eventTypeId, CancellationToken ct)
    {
        await catalog.Delete(eventTypeId, ct);
        return NoContent();
    }
}
