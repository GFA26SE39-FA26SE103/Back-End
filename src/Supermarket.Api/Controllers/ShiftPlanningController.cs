using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;
namespace Supermarket.Api.Controllers;

/// <summary>Shift templates: operational roles read them, the Manager maintains them.</summary>
[ApiController, Authorize, Route("api/shifts")]
public sealed class ShiftTemplatesController(ShiftPlanning planning) : ControllerBase
{
    [HttpGet, Authorize(Roles = AccessRoles.Viewer)] public Task<ShiftTemplateView[]> List(CancellationToken ct) => planning.Templates(ct);
    [HttpPost, Authorize(Roles = AccessRoles.Manager)] public Task<ShiftTemplateView> Create(ShiftTemplateRequest request, CancellationToken ct) => planning.CreateTemplate(request, ct);
    [HttpPatch("{id:guid}"), Authorize(Roles = AccessRoles.Manager)] public Task<ShiftTemplateView> Update(Guid id, ShiftTemplateUpdate request, CancellationToken ct) => planning.UpdateTemplate(id, request, ct);
}

/// <summary>Manager schedule: DRAFT assignments, publishing with Zone coverage, edits and cancellation before a shift starts.</summary>
[ApiController, Authorize, Route("api/schedule")]
public sealed class ScheduleController(ShiftPlanning planning) : ControllerBase
{
    /// <summary>Defaults to the current local Monday–Sunday week; at most 31 days.</summary>
    [HttpGet, Authorize(Roles = AccessRoles.Viewer)] public Task<ScheduleView> Get([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) => planning.Schedule(from, to, ct);
    [HttpGet("staff-options"), Authorize(Roles = AccessRoles.Manager)]
    public Task<StaffOptionView[]> StaffOptions([FromQuery] Guid shiftId, [FromQuery] DateOnly workDate, [FromQuery] Guid? replacingAssignmentId, CancellationToken ct)
        => planning.StaffOptions(shiftId, workDate, replacingAssignmentId, ct);
    [HttpPost("assignments"), Authorize(Roles = AccessRoles.Manager)] public Task<ScheduledShiftView> Assign(AssignShiftRequest request, CancellationToken ct) => planning.Assign(request, ct);
    [HttpPatch("assignments/{id:guid}"), Authorize(Roles = AccessRoles.Manager)] public Task<ScheduledShiftView> Update(Guid id, UpdateAssignmentRequest request, CancellationToken ct) => planning.Update(id, request, ct);
    [HttpPost("assignments/{id:guid}/cancel"), Authorize(Roles = AccessRoles.Manager)] public Task<ScheduledShiftView> Cancel(Guid id, CancelAssignmentRequest request, CancellationToken ct) => planning.Cancel(id, request, ct);
    [HttpPost("publish"), Authorize(Roles = AccessRoles.Manager)] public Task<PublishScheduleResult> Publish(PublishScheduleRequest request, CancellationToken ct) => planning.Publish(request, ct);
}

/// <summary>Approved Staff leave (StaffUnavailability), maintained by the Manager.</summary>
[ApiController, Authorize(Roles = AccessRoles.Manager), Route("api/staff-unavailability")]
public sealed class StaffUnavailabilityController(ShiftPlanning planning) : ControllerBase
{
    [HttpGet] public Task<UnavailabilityView[]> List([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? userId, CancellationToken ct) => planning.Unavailability(from, to, userId, ct);
    [HttpPost] public Task<UnavailabilityView> Add(UnavailabilityRequest request, CancellationToken ct) => planning.AddUnavailability(request, ct);
    [HttpDelete("{id:guid}"), ProducesResponseType(StatusCodes.Status204NoContent)] public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        await planning.RemoveUnavailability(id, ct);
        return NoContent();
    }
}
