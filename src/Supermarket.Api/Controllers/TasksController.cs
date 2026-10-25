using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;
namespace Supermarket.Api.Controllers;

[ApiController, Authorize, Route("api/tasks")]
public sealed class TasksController(StaffTasks staff, OperatorTasks operators) : ControllerBase
{
    [HttpGet, Authorize(Roles = AccessRoles.Viewer)] public Task<StaffTaskView[]> List([FromQuery] string? status, [FromQuery] bool needsAssignment, CancellationToken ct) => operators.List(status, needsAssignment, ct);
    [HttpGet("{taskId:guid}"), Authorize(Roles = AccessRoles.TaskReader)] public Task<StaffTaskView> Get(Guid taskId, CancellationToken ct) => operators.Get(taskId, ct);
    [HttpPost("{taskId:guid}/start"), Authorize(Roles = AccessRoles.Staff)] public Task<StaffTaskView> Start(Guid taskId, CancellationToken ct) => staff.Start(taskId, ct);
    [HttpGet("{taskId:guid}/assignable-staff"), Authorize(Roles = AccessRoles.Operator)] public Task<AssignableStaffView[]> AssignableStaff(Guid taskId, CancellationToken ct) => operators.AssignableStaff(taskId, ct);
    [HttpPost("{taskId:guid}/assign"), Authorize(Roles = AccessRoles.Operator)] public Task<StaffTaskView> Assign(Guid taskId, TaskAssignRequest request, CancellationToken ct) => operators.Assign(taskId, request, ct);
}
