using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = AccessRoles.Viewer), Route("api/operational-events")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OperationalEventsController(OperationalEventHistory events) : ControllerBase
{
    [HttpGet]
    public Task<OperationalEventPage> List([FromQuery] OperationalEventQuery query, CancellationToken ct) => events.List(query, ct);

    [HttpGet("zones")]
    public Task<EventZoneOption[]> Zones(CancellationToken ct) => events.Zones(ct);

    [HttpGet("{eventId:guid}")]
    public Task<OperationalEventDetail> Get(Guid eventId, CancellationToken ct) => events.Get(eventId, ct);
}
