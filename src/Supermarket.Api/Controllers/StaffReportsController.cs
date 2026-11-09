using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize, Route("api/staff-reports")]
public sealed class StaffReportsController(StaffReportWork reports) : ControllerBase
{
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
    [RequestSizeLimit(32L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 32L * 1024 * 1024)]
    public async Task<StaffReportView> Submit([FromForm] StaffReportSubmitForm form, CancellationToken ct)
    {
        var files = form.Photos ?? [];
        if (files.Count > 3) throw new Supermarket.Application.ApplicationException("REPORT_PHOTO_LIMIT", "At most three photos are allowed.", 422);
        var streams = new List<Stream>();
        try
        {
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
                RoutineTaskDecision = form.RoutineTaskDecision }, photos, ct);
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
}
