using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = "ADMIN"), Route("api/cameras/{id:guid}/recorded-video")]
public sealed class RecordedVideosController(RecordedVideoUpload uploads) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(210L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 210L * 1024 * 1024)]
    public async Task<RecordedVideoView> Upload(Guid id, [FromForm] VideoUploadForm form, CancellationToken ct)
    {
        await using var stream = form.File.OpenReadStream();
        return await uploads.Upload(id, stream, form.File.FileName, form.File.Length, ct);
    }
}

public sealed class VideoUploadForm
{
    [Required] public IFormFile File { get; set; } = null!;
}
