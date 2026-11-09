using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = AccessRoles.Operator), Route("api/incidents")]
public sealed class IncidentsController(IncidentActions incidents) : ControllerBase
{
    [HttpPost("{incidentId:guid}/dismiss")]
    public Task<IncidentActionView> Dismiss(Guid incidentId, IncidentDismissRequest request, CancellationToken ct)
        => incidents.Dismiss(incidentId, request, ct);
}
