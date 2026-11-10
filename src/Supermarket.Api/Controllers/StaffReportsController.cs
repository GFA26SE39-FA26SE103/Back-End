using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize, Route("api/staff-reports")]
public sealed class StaffReportsController(StaffReportWork reports, IStaffReportDraftClient draftClient) : ControllerBase
{
    [HttpPost("draft"), Authorize(Roles = AccessRoles.Staff)]
    [RequestSizeLimit(48L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 48L * 1024 * 1024)]
    public async Task<StaffReportDraftView> Draft([FromForm] StaffReportDraftForm form, CancellationToken ct)
    {
        var files = form.Photos ?? [];
        if (files.Count > 3) throw new Supermarket.Application.ApplicationException("REPORT_PHOTO_LIMIT", "At most three photos are allowed.", 422);
        if (form.Note.Length > 4000) throw new Supermarket.Application.ApplicationException("REPORT_NOTE_LIMIT", "Note is too long.", 422);
        if (string.IsNullOrWhiteSpace(form.Note) && files.Count == 0 && form.Voice is null)
            throw new Supermarket.Application.ApplicationException("REPORT_INPUT_REQUIRED", "Provide text, a photo, or voice.", 422);
        var images = new List<StaffReportDraftImage>(files.Count);
        foreach (var file in files)
        {
            if (file.Length is <= 0 or > 10L * 1024 * 1024 || file.ContentType is not ("image/jpeg" or "image/png"))
                throw new Supermarket.Application.ApplicationException("REPORT_PHOTO_INVALID", "Use a JPEG or PNG photo of at most 10 MB.", 422);
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            var bytes = buffer.ToArray();
            var valid = file.ContentType == "image/jpeg" && bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })
                || file.ContentType == "image/png" && bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            if (!valid) throw new Supermarket.Application.ApplicationException("REPORT_PHOTO_INVALID", "Photo content does not match JPEG or PNG.", 422);
            images.Add(new StaffReportDraftImage(file.ContentType, Convert.ToBase64String(bytes)));
        }
        StaffReportDraftAudio? voice = null;
        string? transcript = null;
        if (form.Voice is { } recording)
        {
            if (recording.Length is <= 0 or > StaffReportWork.MaxVoiceBytes)
                throw new Supermarket.Application.ApplicationException("REPORT_VOICE_SIZE", "Voice note must be at most 10 MB.", 422);
            await using var stream = recording.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            voice = new StaffReportDraftAudio(buffer.ToArray());
            transcript = await draftClient.Transcribe(voice, ct);
        }
        var types = await reports.IncidentTypes(ct);
        var tasks = await reports.TaskTypes(ct);
        var locationText = string.Join("\n", new[] { form.Note, transcript }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var mentionedZones = (await reports.Zones(null, ct)).Where(x =>
            x.Name.Length >= 3 && locationText.Contains(x.Name, StringComparison.OrdinalIgnoreCase)
            || x.Code.Length >= 3 && locationText.Contains(x.Code, StringComparison.OrdinalIgnoreCase)).Take(21).ToArray();
        var candidates = mentionedZones.Length <= 20 ? mentionedZones : [];
        var draft = await draftClient.Analyze(new StaffReportDraftRequest(form.Note, images.ToArray(), types)
        { TaskTypes = tasks, ZoneCandidates = candidates, Transcript = transcript }, ct);
        var validType = types.Any(x => x.Code == draft.IncidentTypeCode);
        var category = draft.ReportCategory is "POTENTIAL_INCIDENT" or "ROUTINE_REQUEST" ? draft.ReportCategory : null;
        var allowedCodes = category == "POTENTIAL_INCIDENT" ? types.FirstOrDefault(x => x.Code == draft.IncidentTypeCode)?.AllowedTaskTypeCodes ?? []
            : tasks.Where(x => x.RoutineEligible).Select(x => x.Code).ToArray();
        return draft with { ReportCategory = category,
            IncidentTypeCode = category == "POTENTIAL_INCIDENT" && validType ? draft.IncidentTypeCode : null,
            SuggestedTaskTypeCodes = (draft.SuggestedTaskTypeCodes ?? []).Where(code => allowedCodes.Contains(code)).Take(8).ToArray(),
            SuggestedZoneId = candidates.Any(x => x.ZoneId == draft.SuggestedZoneId) ? draft.SuggestedZoneId : null,
            NeedsReview = true };
    }

    [HttpGet("incident-types"), Authorize(Roles = AccessRoles.Staff)]
    public Task<StaffReportIncidentTypeView[]> IncidentTypes(CancellationToken ct) => reports.IncidentTypes(ct);

    [HttpGet("task-types"), Authorize(Roles = AccessRoles.Staff)]
    public Task<StaffReportTaskTypeView[]> TaskTypes(CancellationToken ct) => reports.TaskTypes(ct);

    [HttpGet("zones"), Authorize(Roles = AccessRoles.Staff)]
    public Task<StaffReportZoneView[]> Zones([FromQuery] Guid? floorId, CancellationToken ct) => reports.Zones(floorId, ct);

    [HttpGet("duplicates"), Authorize(Roles = AccessRoles.Staff)]
    public Task<StaffReportDuplicateView[]> Duplicates([FromQuery] Guid zoneId, [FromQuery] string incidentTypeCode, CancellationToken ct)
        => reports.Duplicates(zoneId, incidentTypeCode, ct);

    [HttpGet("routine-overlaps"), Authorize(Roles = AccessRoles.Staff)]
    public Task<StaffReportRoutineOverlapView[]> RoutineOverlaps([FromQuery] Guid zoneId, [FromQuery] string taskTypeCode, CancellationToken ct)
        => reports.RoutineOverlaps(zoneId, taskTypeCode, ct);

    [HttpPost, Authorize(Roles = AccessRoles.Staff)]
    // Three 10 MB photos plus one 10 MB voice note, with room for the form fields.
    [RequestSizeLimit(48L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 48L * 1024 * 1024)]
    public async Task<StaffReportView> Submit([FromForm] StaffReportSubmitForm form, CancellationToken ct)
    {
        var files = form.Photos ?? [];
        if (files.Count > 3) throw new Supermarket.Application.ApplicationException("REPORT_PHOTO_LIMIT", "At most three photos are allowed.", 422);
        var streams = new List<Stream>();
        try
        {
            StaffReportAudio? voice = null;
            if (form.Voice is { } recording)
            {
                var voiceStream = recording.OpenReadStream();
                streams.Add(voiceStream);
                voice = new StaffReportAudio(voiceStream, recording.FileName, recording.ContentType, recording.Length);
            }
            var photos = new List<StaffReportPhoto>(files.Count);
            foreach (var file in files)
            {
                var stream = file.OpenReadStream();
                streams.Add(stream);
                photos.Add(new StaffReportPhoto(stream, file.FileName, file.ContentType, file.Length));
            }
            return await reports.Submit(new StaffReportInput(form.ReportId, form.ZoneId, form.Title, form.Description, form.LocationDescription,
                form.IncidentTypeCode, form.DuplicateDecision) { ReportCategory = form.ReportCategory,
                TaskTypeCodes = form.TaskTypeCodes?.ToArray(), ConfirmedScenarioCode = form.ConfirmedScenarioCode,
                RoutineTaskDecision = form.RoutineTaskDecision }, photos, voice, ct);
        }
        finally { foreach (var stream in streams) await stream.DisposeAsync(); }
    }

    [HttpGet("{reportId:guid}")] public Task<StaffReportView> Get(Guid reportId, CancellationToken ct) => reports.Get(reportId, ct);

    [HttpGet("{reportId:guid}/media/{mediaId:guid}")]
    public async Task<IActionResult> Image(Guid reportId, Guid mediaId, CancellationToken ct)
    {
        var file = await reports.Image(reportId, mediaId, ct);
        Response.Headers.CacheControl = "private, no-store";
        return File(file.Content, file.ContentType);
    }

    [HttpPost("{reportId:guid}/qualify-incident"), Authorize(Roles = AccessRoles.Operator)]
    public Task<StaffReportIncidentView> QualifyIncident(Guid reportId, CancellationToken ct)
        => reports.QualifyIncident(reportId, ct);

    [HttpPut("{reportId:guid}/duplicate-decision"), Authorize(Roles = AccessRoles.Staff)]
    public Task<StaffReportView> SetDuplicateDecision(Guid reportId, StaffReportDuplicateDecisionRequest request, CancellationToken ct)
        => reports.SetDuplicateDecision(reportId, request, ct);
}

public sealed class StaffReportDraftForm
{
    public string Note { get; set; } = "";
    public List<IFormFile>? Photos { get; set; }
    public IFormFile? Voice { get; set; }
}

public sealed class StaffReportSubmitForm
{
    [Required] public Guid ReportId { get; set; }
    [Required] public Guid ZoneId { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    [Required, MaxLength(32)] public string ReportCategory { get; set; } = "";
    [MaxLength(4000)] public string? Description { get; set; }
    [MaxLength(1000)] public string? LocationDescription { get; set; }
    [MaxLength(50)] public string? IncidentTypeCode { get; set; }
    public List<string>? TaskTypeCodes { get; set; }
    [MaxLength(50)] public string? ConfirmedScenarioCode { get; set; }
    [MaxLength(32)] public string? DuplicateDecision { get; set; }
    [MaxLength(32)] public string? RoutineTaskDecision { get; set; }
    public List<IFormFile>? Photos { get; set; }
    /// <summary>Optional recorded voice note (M4A/AAC, MP3, WAV, OGG or WEBM, at most 10 MB).</summary>
    public IFormFile? Voice { get; set; }
}
