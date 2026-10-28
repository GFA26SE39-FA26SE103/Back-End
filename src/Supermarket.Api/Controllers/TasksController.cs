using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;
namespace Supermarket.Api.Controllers;

[ApiController, Authorize, Route("api/tasks")]
public sealed class TasksController(StaffTasks staff, OperatorTasks operators, TaskEvidenceWork evidence) : ControllerBase
{
    [HttpGet, Authorize(Roles = AccessRoles.Viewer)] public Task<StaffTaskView[]> List([FromQuery] string? status, [FromQuery] bool needsAssignment, CancellationToken ct) => operators.List(status, needsAssignment, ct);
    [HttpGet("{taskId:guid}"), Authorize(Roles = AccessRoles.TaskReader)] public Task<StaffTaskView> Get(Guid taskId, CancellationToken ct) => operators.Get(taskId, ct);
    [HttpPost("{taskId:guid}/start"), Authorize(Roles = AccessRoles.Staff)] public Task<StaffTaskView> Start(Guid taskId, CancellationToken ct) => staff.Start(taskId, ct);
    [HttpGet("{taskId:guid}/assignable-staff"), Authorize(Roles = AccessRoles.Operator)] public Task<AssignableStaffView[]> AssignableStaff(Guid taskId, CancellationToken ct) => operators.AssignableStaff(taskId, ct);
    [HttpPost("{taskId:guid}/assign"), Authorize(Roles = AccessRoles.Operator)] public Task<StaffTaskView> Assign(Guid taskId, TaskAssignRequest request, CancellationToken ct) => operators.Assign(taskId, request, ct);

    // MF-03 completion: Staff uploads photos and submits; an independent Operator verifies or rejects.
    [HttpGet("{taskId:guid}/evidence"), Authorize(Roles = AccessRoles.TaskReader)] public Task<TaskEvidenceView> Evidence(Guid taskId, CancellationToken ct) => evidence.Get(taskId, ct);

    [HttpPost("{taskId:guid}/evidence"), Authorize(Roles = AccessRoles.Staff)]
    [RequestSizeLimit(11L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 11L * 1024 * 1024)]
    public async Task<TaskEvidenceView> UploadEvidence(Guid taskId, [FromForm] TaskEvidenceUploadForm form, CancellationToken ct)
    {
        await using var stream = form.File.OpenReadStream();
        return await evidence.Upload(taskId, form.Type, stream, form.File.FileName, form.File.ContentType, form.File.Length, ct);
    }

    [HttpDelete("{taskId:guid}/evidence/{evidenceId:guid}"), Authorize(Roles = AccessRoles.Staff)]
    public Task<TaskEvidenceView> RemoveEvidence(Guid taskId, Guid evidenceId, CancellationToken ct) => evidence.RemoveDraft(taskId, evidenceId, ct);

    [HttpGet("{taskId:guid}/evidence/{evidenceId:guid}/image"), Authorize(Roles = AccessRoles.TaskReader)]
    public async Task<IActionResult> EvidenceImage(Guid taskId, Guid evidenceId, CancellationToken ct)
    {
        var file = await evidence.Image(taskId, evidenceId, ct);
        Response.Headers.CacheControl = "private, no-store";
        return File(file.Content, file.ContentType);
    }

    [HttpPost("{taskId:guid}/submit"), Authorize(Roles = AccessRoles.Staff)] public Task<TaskCompletionResult> Submit(Guid taskId, TaskSubmitRequest request, CancellationToken ct) => evidence.Submit(taskId, request, ct);
    [HttpPost("{taskId:guid}/verify"), Authorize(Roles = AccessRoles.Operator)] public Task<TaskCompletionResult> Verify(Guid taskId, TaskVerifyRequest request, CancellationToken ct) => evidence.Verify(taskId, request, ct);
    [HttpPost("{taskId:guid}/reject"), Authorize(Roles = AccessRoles.Operator)] public Task<TaskCompletionResult> Reject(Guid taskId, TaskRejectRequest request, CancellationToken ct) => evidence.Reject(taskId, request, ct);
}

public sealed class TaskEvidenceUploadForm
{
    [Required] public IFormFile File { get; set; } = null!;
    /// <summary>BEFORE_PHOTO or AFTER_PHOTO.</summary>
    [Required] public string Type { get; set; } = "";
}
