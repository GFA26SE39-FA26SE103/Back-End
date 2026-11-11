using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = AccessRoles.Manager), Route("api/manager/analytics")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ManagerAnalyticsController(ManagerAnalytics analytics, ManagerAnalyticsExport export) : ControllerBase
{
    [HttpGet("overview")]
    public Task<AnalyticsOverview> Overview([FromQuery] AnalyticsQuery query, CancellationToken ct) => analytics.Overview(query, ct);

    [HttpGet("trends")]
    public Task<AnalyticsTrends> Trends([FromQuery] AnalyticsQuery query, CancellationToken ct) => analytics.Trends(query, ct);

    [HttpGet("zones")]
    public Task<AnalyticsZones> Zones([FromQuery] AnalyticsQuery query, CancellationToken ct) => analytics.Zones(query, ct);

    [HttpGet("export")]
    [Produces("text/csv", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/pdf")]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK, "text/csv", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/pdf")]
    public async Task<IActionResult> Export([FromQuery] AnalyticsQuery query, [FromQuery] string format = "csv", CancellationToken ct = default)
    {
        var file = await export.Generate(query, format, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }
}
