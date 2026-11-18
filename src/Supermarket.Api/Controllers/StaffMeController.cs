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
}
