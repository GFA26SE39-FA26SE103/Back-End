using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;
namespace Supermarket.Api.Controllers;

/// <summary>SWAP/COVER requests: Staff A requests, Staff B answers, a Manager decides.</summary>
[ApiController, Authorize, Route("api/swap-requests")]
public sealed class ShiftSwapsController(ShiftSwaps swaps) : ControllerBase
{
    /// <summary>Manager queue; PENDING_MANAGER by default, or a comma list of statuses, or ALL.</summary>
    [HttpGet, Authorize(Roles = AccessRoles.Manager)] public Task<SwapRequestView[]> List([FromQuery] string? status, CancellationToken ct) => swaps.ForManager(status, ct);
    [HttpPost, Authorize(Roles = AccessRoles.Staff)] public Task<SwapRequestView> Create(SwapCreateRequest request, CancellationToken ct) => swaps.Create(request, ct);
    [HttpGet("{id:guid}"), Authorize(Roles = AccessRoles.SwapReader)] public Task<SwapRequestView> Get(Guid id, CancellationToken ct) => swaps.Get(id, ct);
    [HttpPost("{id:guid}/respond"), Authorize(Roles = AccessRoles.Staff)] public Task<SwapRequestView> Respond(Guid id, SwapRespondRequest request, CancellationToken ct) => swaps.Respond(id, request, ct);
    [HttpPost("{id:guid}/cancel"), Authorize(Roles = AccessRoles.Staff)] public Task<SwapRequestView> Cancel(Guid id, SwapCancelRequest request, CancellationToken ct) => swaps.Cancel(id, request, ct);
    [HttpPost("{id:guid}/approve"), Authorize(Roles = AccessRoles.Manager)] public Task<SwapRequestView> Approve(Guid id, SwapDecisionRequest request, CancellationToken ct) => swaps.Approve(id, request, ct);
    [HttpPost("{id:guid}/reject"), Authorize(Roles = AccessRoles.Manager)] public Task<SwapRequestView> Reject(Guid id, SwapDecisionRequest request, CancellationToken ct) => swaps.Reject(id, request, ct);
    [HttpGet("{id:guid}/manager-candidates"), Authorize(Roles = AccessRoles.Manager)] public Task<SwapCandidatesView> ManagerCandidates(Guid id, CancellationToken ct) => swaps.ManagerCandidates(id, ct);
    [HttpPost("{id:guid}/assign-cover"), Authorize(Roles = AccessRoles.Manager)] public Task<SwapRequestView> AssignCover(Guid id, ManagerAssignCoverRequest request, CancellationToken ct) => swaps.AssignCover(id, request, ct);
    [HttpPost("manager-direct"), Authorize(Roles = AccessRoles.Manager)] public Task<SwapRequestView> Direct(ManagerDirectChangeRequest request, CancellationToken ct) => swaps.Direct(request, ct);
}

/// <summary>The caller's own SWAP/COVER requests and eligible colleagues.</summary>
[ApiController, Authorize(Roles = AccessRoles.Staff), Route("api/me")]
public sealed class StaffSwapsController(ShiftSwaps swaps) : ControllerBase
{
    /// <summary>box = sent | received; both when omitted.</summary>
    [HttpGet("swap-requests")] public Task<SwapRequestView[]> Mine([FromQuery] string? box, CancellationToken ct) => swaps.Mine(box, ct);
    /// <summary>Eligible colleagues for one of the caller's shifts; type = SWAP (default) or COVER.</summary>
    [HttpGet("shifts/{shiftAssignmentId:guid}/swap-candidates")] public Task<SwapCandidatesView> Candidates(Guid shiftAssignmentId, [FromQuery] string? type, CancellationToken ct)
        => swaps.Candidates(shiftAssignmentId, type, ct);
    [HttpPost("swap-requests/manager-help")] public Task<SwapRequestView> ManagerHelp(ManagerHelpRequest request, CancellationToken ct) => swaps.RequestManagerHelp(request, ct);
}
