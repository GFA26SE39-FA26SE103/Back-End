using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;
namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = "ADMIN"), Route("api/incident-types")]
public sealed class IncidentTypesController(MonitoringSetup setup, IncidentTypeCatalog catalog) : ControllerBase
{
    [HttpGet] public Task<IncidentTypeView[]> List(CancellationToken ct) => setup.IncidentTypes(ct);
    [HttpPost] public Task<IncidentTypeAdminView> Create(IncidentTypeCreateRequest request, CancellationToken ct) => catalog.Create(request, ct);
    [HttpPut("{incidentTypeId:guid}")] public Task<IncidentTypeAdminView> Update(Guid incidentTypeId, IncidentTypeUpdateRequest request, CancellationToken ct)
        => catalog.Update(incidentTypeId, request, ct);
}
