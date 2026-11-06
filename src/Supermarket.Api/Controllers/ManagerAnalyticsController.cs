using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = AccessRoles.Manager), Route("api/manager/analytics")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ManagerAnalyticsController(ManagerAnalytics analytics) : ControllerBase
{
    [HttpGet("overview")]
    public Task<AnalyticsOverview> Overview([FromQuery] AnalyticsQuery query, CancellationToken ct) => analytics.Overview(query, ct);

    [HttpGet("trends")]
    public Task<AnalyticsTrends> Trends([FromQuery] AnalyticsQuery query, CancellationToken ct) => analytics.Trends(query, ct);

    [HttpGet("zones")]
    public Task<AnalyticsZones> Zones([FromQuery] AnalyticsQuery query, CancellationToken ct) => analytics.Zones(query, ct);
}
