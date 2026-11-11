using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = AccessRoles.Operator), Route("api/incidents")]
public sealed class IncidentsController(IncidentActions incidents) : ControllerBase
{
    /// <summary>L3: Operator Incident board; state = open (default) | ended | all.</summary>
    [HttpGet]
    public Task<IncidentListView[]> List([FromQuery] string? state, CancellationToken ct) => incidents.List(state, ct);

    [HttpGet("{incidentId:guid}")]
    public Task<IncidentListView> Get(Guid incidentId, CancellationToken ct) => incidents.Get(incidentId, ct);

    [HttpPost("{incidentId:guid}/dismiss")]
    public Task<IncidentActionView> Dismiss(Guid incidentId, IncidentDismissRequest request, CancellationToken ct)
        => incidents.Dismiss(incidentId, request, ct);
}
