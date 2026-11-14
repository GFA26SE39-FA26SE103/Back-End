using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

/// <summary>FE-12 §3.13.3: read-only audit-log inspection for Administrators.</summary>
[ApiController, Authorize(Roles = AccessRoles.Admin), Route("api/admin/audit-logs")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuditLogsController(AuditLogs logs) : ControllerBase
{
    [HttpGet]
    public Task<AuditLogPage> List([FromQuery] AuditLogQuery query, CancellationToken ct) => logs.List(query, ct);

    [HttpGet("facets")]
    public Task<AuditLogFacets> Facets(CancellationToken ct) => logs.Facets(ct);

    [HttpGet("{auditLogId:guid}")]
    public Task<AuditLogView> Get(Guid auditLogId, CancellationToken ct) => logs.Get(auditLogId, ct);
}
