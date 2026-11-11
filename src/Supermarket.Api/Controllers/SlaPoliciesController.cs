using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = AccessRoles.Admin), Route("api/sla-policies")]
public sealed class SlaPoliciesController(IncidentSlaPolicies policies, SlaEscalationPolicies escalation) : ControllerBase
{
    [HttpGet] public Task<IncidentSlaPolicyView[]> List(CancellationToken ct) => policies.List(ct);

    /// <summary>L7: roles notified at the SLA warning milestone and at breach for any policy (Incident, OperationalEvent or TaskType).</summary>
    [HttpGet("{policyId:guid}/escalation")]
    public Task<SlaEscalationView> Escalation(Guid policyId, CancellationToken ct) => escalation.Get(policyId, ct);

    [HttpPut("{policyId:guid}/escalation")]
    public Task<SlaEscalationView> SaveEscalation(Guid policyId, SlaEscalationSaveRequest request, CancellationToken ct)
        => escalation.Save(policyId, request, ct);
}

/// <summary>L7: SLA timing of each OperationalEventType; WARNING/CRITICAL actions stay under /api/monitoring-rules/{ruleId}/response-policies.</summary>
[ApiController, Authorize(Roles = AccessRoles.Admin), Route("api/operational-event-types")]
public sealed class OperationalEventSlaPoliciesController(OperationalEventSlaPolicies policies) : ControllerBase
{
    [HttpGet("sla-policies")] public Task<OperationalEventSlaPolicyView[]> List(CancellationToken ct) => policies.List(ct);

    [HttpPut("{eventTypeId:guid}/sla-policy")]
    public Task<OperationalEventSlaPolicyView> Save(Guid eventTypeId, SlaTimingSaveRequest request, CancellationToken ct)
        => policies.Save(eventTypeId, request, ct);
}

/// <summary>L1/L8: SLA of StaffReport routine and Operator manual Tasks per TaskType.</summary>
[ApiController, Authorize(Roles = AccessRoles.Admin), Route("api/task-types")]
public sealed class TaskTypeSlaPoliciesController(TaskTypeSlaPolicies policies) : ControllerBase
{
    [HttpGet("sla-policies")] public Task<TaskTypeSlaPolicyView[]> List(CancellationToken ct) => policies.List(ct);

    [HttpPut("{taskTypeId:guid}/sla-policy")]
    public Task<TaskTypeSlaPolicyView> Save(Guid taskTypeId, SlaTimingSaveRequest request, CancellationToken ct)
        => policies.Save(taskTypeId, request, ct);
}

[ApiController, Authorize(Roles = AccessRoles.Admin), Route("api/incident-types/{incidentTypeId:guid}/sla-policy")]
public sealed class IncidentTypeSlaPolicyController(IncidentSlaPolicies policies) : ControllerBase
{
    [HttpPut] public Task<IncidentSlaPolicyView> Save(Guid incidentTypeId, IncidentSlaPolicySaveRequest request, CancellationToken ct)
        => policies.Save(incidentTypeId, request, ct);
}
