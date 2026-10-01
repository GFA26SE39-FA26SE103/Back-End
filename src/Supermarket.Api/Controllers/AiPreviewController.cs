using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = "ADMIN"), Route("api/cameras/{id:guid}/ai-preview")]
public sealed class AiPreviewController(AiPreview preview) : ControllerBase
{
    [HttpPost("start")]
    public Task<AiPreviewStatusView> Start(Guid id, CancellationToken ct) => preview.Start(id, ct);

    [HttpGet("status")]
    public Task<AiPreviewStatusView> Status(Guid id, CancellationToken ct) => preview.Status(id, ct);

    [HttpGet("frame")]
    public async Task<IActionResult> Frame(Guid id, CancellationToken ct)
    {
        var frame = await preview.Frame(id, ct);
        Response.Headers.CacheControl = "no-store";
        return File(frame.Bytes, frame.ContentType);
    }

    [HttpPost("stop")]
    public Task<AiPreviewStatusView> Stop(Guid id, CancellationToken ct) => preview.Stop(id, ct);
}
