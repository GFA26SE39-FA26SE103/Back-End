using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;
namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = "ADMIN"), Route("api/incident-types")]
public sealed class IncidentTypesController(MonitoringSetup setup) : ControllerBase
{
    [HttpGet] public Task<IncidentTypeView[]> List(CancellationToken ct) => setup.IncidentTypes(ct);
}
