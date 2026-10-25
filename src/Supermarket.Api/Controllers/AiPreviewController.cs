using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

public sealed record ReleaseAiPreviewRequest(Guid ViewerLeaseId);

[ApiController, Authorize(Roles = AccessRoles.LiveView), Route("api/cameras/{id:guid}/ai-preview")]
public sealed class AiPreviewController(AiPreview preview) : ControllerBase
{
    [HttpPost("start")]
    public Task<AiPreviewStatusView> Start(Guid id, CancellationToken ct, [FromQuery] Guid? zoneId = null) => preview.Start(id, ct, zoneId);

    [HttpGet("status")]
    public Task<AiPreviewStatusView> Status(Guid id, [FromHeader(Name="X-AI-Viewer-Lease")] Guid viewerLeaseId, CancellationToken ct) => preview.Status(id, viewerLeaseId, ct);

    [HttpGet("frame")]
    public async Task<IActionResult> Frame(Guid id, [FromHeader(Name="X-AI-Viewer-Lease")] Guid viewerLeaseId, CancellationToken ct)
    {
        var frame = await preview.Frame(id, viewerLeaseId, ct);
        Response.Headers.CacheControl = "no-store";
        return File(frame.Bytes, frame.ContentType);
    }

    [HttpGet("frame/next")]
    [Produces("image/jpeg")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> NextFrame(Guid id, [FromHeader(Name="X-AI-Viewer-Lease")] Guid viewerLeaseId, CancellationToken ct, [FromQuery] long afterSequence = 0, [FromQuery] Guid? afterSessionId = null)
    {
        if (afterSequence < 0) return BadRequest();
        var frame = await preview.NextFrame(id, viewerLeaseId, afterSequence, afterSessionId, ct);
        Response.Headers.CacheControl = "no-store";
        if (frame is null) return NoContent();
        Response.Headers["X-Frame-Sequence"] = frame.FrameSequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Response.Headers["X-Session-Id"] = frame.SessionId.ToString();
        return File(frame.Bytes, frame.ContentType);
    }

    [HttpPost("stop")]
    public Task<AiPreviewStatusView> Stop(Guid id, [FromBody] ReleaseAiPreviewRequest request, CancellationToken ct) => preview.Stop(id, request.ViewerLeaseId, ct);
}
