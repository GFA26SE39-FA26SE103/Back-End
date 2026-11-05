using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = AccessRoles.Operator), Route("api/operator/task-offers")]
public sealed class OperatorTaskOffersController(OperatorTasks tasks) : ControllerBase
{
    [HttpGet] public Task<TaskOfferView[]> List([FromQuery] string? status, [FromQuery] Guid? taskId, CancellationToken ct) => tasks.Offers(status, taskId, ct);
    [HttpGet("{offerId:guid}")] public Task<TaskOfferView> Get(Guid offerId, CancellationToken ct) => tasks.Offer(offerId, ct);
}
