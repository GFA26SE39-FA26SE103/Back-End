using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = AccessRoles.Admin), Route("api/sla-policies")]
public sealed class SlaPoliciesController(IncidentSlaPolicies policies) : ControllerBase
{
    [HttpGet] public Task<IncidentSlaPolicyView[]> List(CancellationToken ct) => policies.List(ct);
}

[ApiController, Authorize(Roles = AccessRoles.Admin), Route("api/incident-types/{incidentTypeId:guid}/sla-policy")]
public sealed class IncidentTypeSlaPolicyController(IncidentSlaPolicies policies) : ControllerBase
{
    [HttpPut] public Task<IncidentSlaPolicyView> Save(Guid incidentTypeId, IncidentSlaPolicySaveRequest request, CancellationToken ct)
        => policies.Save(incidentTypeId, request, ct);
}
