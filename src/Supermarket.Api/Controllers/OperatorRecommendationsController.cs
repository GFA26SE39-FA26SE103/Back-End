using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = AccessRoles.Operator), Route("api/operator/recommendations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OperatorRecommendationsController(RecommendationWork recommendations) : ControllerBase
{
    [HttpGet]
    public Task<RecommendationPage> List([FromQuery] OperatorRecommendationQuery query, CancellationToken ct)
        => recommendations.ListAccepted(query, ct);

    [HttpGet("{id:guid}")]
    public Task<RecommendationView> Get(Guid id, CancellationToken ct) => recommendations.GetAccepted(id, ct);
}
