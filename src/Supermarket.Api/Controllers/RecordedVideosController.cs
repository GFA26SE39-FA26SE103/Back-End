using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Infrastructure.Ai;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = "ADMIN"), Route("api/cameras/{id:guid}/recorded-video")]
public sealed class RecordedVideosController(RecordedVideoUpload uploads, RecordedVideoPlayback playback, IOptions<AiPreviewOptions> ai) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(210L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 210L * 1024 * 1024)]
    public async Task<RecordedVideoView> Upload(Guid id, [FromForm] VideoUploadForm form, CancellationToken ct)
    {
        await using var stream = form.File.OpenReadStream();
        return await uploads.Upload(id, stream, form.File.FileName, form.File.Length, ct);
    }

    [AllowAnonymous, HttpGet("content/{assetId:guid}")]
    public async Task<IActionResult> ReadContent(Guid id, Guid assetId, [FromQuery] string? ticket, CancellationToken ct)
    {
        var key = ai.Value.InternalServiceKey;
        var supplied = Request.Headers["X-AI-Service-Key"].ToString();
        var aiAuthorized = !string.IsNullOrWhiteSpace(ai.Value.RecordedVideoBaseUrl) && !string.IsNullOrEmpty(key)
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(supplied));
        Response.Headers.CacheControl = "private, no-store";
        var stream = await playback.Open(id, assetId, ticket, aiAuthorized, ct);
        return File(stream, "video/mp4", enableRangeProcessing: true);
    }
}

public sealed class VideoUploadForm
{
    [Required] public IFormFile File { get; set; } = null!;
}
