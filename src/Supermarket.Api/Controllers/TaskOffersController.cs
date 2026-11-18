using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;
namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = AccessRoles.Staff), Route("api/task-offers")]
public sealed class TaskOffersController(StaffTasks staff) : ControllerBase
{
    [HttpGet("{offerId:guid}")] public Task<TaskOfferView> Get(Guid offerId, CancellationToken ct) => staff.Offer(offerId, ct);
    [HttpPost("{offerId:guid}/respond")] public Task<TaskResponseResult> Respond(Guid offerId, TaskResponseRequest request, CancellationToken ct) => staff.Respond(offerId, request, ct);
}
