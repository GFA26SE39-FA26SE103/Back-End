using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;
namespace Supermarket.Api.Controllers;

[ApiController,Authorize(Roles=AccessRoles.LiveView),Route("api/cameras/{id:guid}")]
public sealed class MonitoringLiveController(MonitoringLive live):ControllerBase
{
    [HttpGet("monitoring-runtime")]
    [ProducesResponseType<MonitoringCameraRuntimeView>(200)]
    [ProducesResponseType<ProblemDetails>(401),ProducesResponseType<ProblemDetails>(403),ProducesResponseType<ProblemDetails>(404),ProducesResponseType<ProblemDetails>(503)]
    public Task<MonitoringCameraRuntimeView> Runtime(Guid id,CancellationToken ct)=>live.Runtime(id,ct);
    [HttpPost("monitoring-runtime/replay")]
    [ProducesResponseType(204)]
    [ProducesResponseType<ProblemDetails>(401),ProducesResponseType<ProblemDetails>(403),ProducesResponseType<ProblemDetails>(404),ProducesResponseType<ProblemDetails>(409),ProducesResponseType<ProblemDetails>(503)]
    public async Task<IActionResult> Replay(Guid id,RecordedMonitoringReplayRequest request,CancellationToken ct)
    {
        await live.Replay(id,request.SessionId,ct);
        return NoContent();
    }
    [HttpGet("incidents")]
    [ProducesResponseType<IncidentFeedView>(200)]
    [ProducesResponseType<ProblemDetails>(400),ProducesResponseType<ProblemDetails>(401),ProducesResponseType<ProblemDetails>(403),ProducesResponseType<ProblemDetails>(404),ProducesResponseType<ProblemDetails>(503)]
    public Task<IncidentFeedView> Incidents(Guid id,CancellationToken ct,[FromQuery] int limit=20,[FromQuery] DateTime? afterCreatedAt=null,
        [FromQuery] Guid? afterIncidentId=null,[FromQuery] bool includeEnded=false)
        =>live.Incidents(id,new(limit,afterCreatedAt,afterIncidentId,includeEnded),ct);
}

public sealed record RecordedMonitoringReplayRequest(Guid SessionId);
