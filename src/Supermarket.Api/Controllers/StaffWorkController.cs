using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;
namespace Supermarket.Api.Controllers;

/// <summary>Staff mobile routes. Every route returns only the caller's own data.</summary>
[ApiController, Authorize(Roles = AccessRoles.Staff), Route("api/me")]
public sealed class StaffMeController(StaffTasks staff) : ControllerBase
{
    [HttpGet("shifts")] public Task<ShiftView[]> Shifts([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) => staff.Shifts(from, to, ct);
    [HttpPost("shifts/{shiftAssignmentId:guid}/check-in")] public Task<ShiftView> CheckIn(Guid shiftAssignmentId, CancellationToken ct) => staff.CheckIn(shiftAssignmentId, ct);
    [HttpGet("eligibility")] public Task<EligibilityView> Eligibility(CancellationToken ct) => staff.Eligibility(ct);
    /// <summary>Open offers by default; status=ALL also returns offers from the last 24 hours with their outcome.</summary>
    [HttpGet("task-offers")] public Task<TaskOfferView[]> Offers([FromQuery] string? status, CancellationToken ct) => staff.Offers(!string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase), ct);
    [HttpGet("tasks")] public Task<StaffTaskView[]> Tasks([FromQuery] string? status, CancellationToken ct) => staff.Tasks(status, ct);
    [HttpGet("notifications")] public Task<NotificationView[]> Notifications(CancellationToken ct) => staff.Notifications(ct);
    [HttpPost("notifications/read-all"), ProducesResponseType(StatusCodes.Status204NoContent)] public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        await staff.ReadAll(ct);
        return NoContent();
    }
}

[ApiController, Authorize(Roles = AccessRoles.Staff), Route("api/task-offers")]
public sealed class TaskOffersController(StaffTasks staff) : ControllerBase
{
    [HttpGet("{offerId:guid}")] public Task<TaskOfferView> Get(Guid offerId, CancellationToken ct) => staff.Offer(offerId, ct);
    [HttpPost("{offerId:guid}/respond")] public Task<TaskResponseResult> Respond(Guid offerId, TaskResponseRequest request, CancellationToken ct) => staff.Respond(offerId, request, ct);
}

[ApiController, Authorize, Route("api/tasks")]
public sealed class TasksController(StaffTasks staff, OperatorTasks operators) : ControllerBase
{
    [HttpGet, Authorize(Roles = AccessRoles.Viewer)] public Task<StaffTaskView[]> List([FromQuery] string? status, [FromQuery] bool needsAssignment, CancellationToken ct) => operators.List(status, needsAssignment, ct);
    [HttpGet("{taskId:guid}"), Authorize(Roles = AccessRoles.TaskReader)] public Task<StaffTaskView> Get(Guid taskId, CancellationToken ct) => operators.Get(taskId, ct);
    [HttpPost("{taskId:guid}/start"), Authorize(Roles = AccessRoles.Staff)] public Task<StaffTaskView> Start(Guid taskId, CancellationToken ct) => staff.Start(taskId, ct);
    [HttpGet("{taskId:guid}/assignable-staff"), Authorize(Roles = AccessRoles.Operator)] public Task<AssignableStaffView[]> AssignableStaff(Guid taskId, CancellationToken ct) => operators.AssignableStaff(taskId, ct);
    [HttpPost("{taskId:guid}/assign"), Authorize(Roles = AccessRoles.Operator)] public Task<StaffTaskView> Assign(Guid taskId, TaskAssignRequest request, CancellationToken ct) => operators.Assign(taskId, request, ct);
}
