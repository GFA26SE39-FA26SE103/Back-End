using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = AccessRoles.Manager), Route("api/manager/recommendations")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ManagerRecommendationsController(RecommendationWork recommendations) : ControllerBase
{
    [HttpPost("generate")]
    public Task<RecommendationGenerationResult> Generate(GenerateRecommendationRequest request, CancellationToken ct)
        => recommendations.Generate(request, ct);

    [HttpGet]
    public Task<RecommendationPage> List([FromQuery] RecommendationQuery query, CancellationToken ct) => recommendations.List(query, ct);

    [HttpGet("{id:guid}")]
    public Task<RecommendationView> Get(Guid id, CancellationToken ct) => recommendations.Get(id, ct);

    [HttpPost("{id:guid}/decision")]
    public Task<RecommendationView> Decide(Guid id, RecommendationDecision request, CancellationToken ct) => recommendations.Decide(id, request, ct);
}
