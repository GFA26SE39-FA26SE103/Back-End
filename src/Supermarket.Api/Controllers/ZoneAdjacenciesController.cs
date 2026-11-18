using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize, Route("api/zones/{zoneId:guid}/adjacent-zones")]
public sealed class ZoneAdjacenciesController(ZoneAdjacencies adjacencies) : ControllerBase
{
    [Authorize(Roles = AccessRoles.Viewer), HttpGet]
    public Task<List<AdjacentZoneView>> List(Guid zoneId, CancellationToken ct) =>
        adjacencies.List(zoneId, ct);

    [Authorize(Roles = AccessRoles.Admin), HttpPut]
    public Task<List<AdjacentZoneView>> Replace(
        Guid zoneId,
        ZoneAdjacencyReplaceRequest request,
        CancellationToken ct) => adjacencies.Replace(zoneId, request, ct);
}
