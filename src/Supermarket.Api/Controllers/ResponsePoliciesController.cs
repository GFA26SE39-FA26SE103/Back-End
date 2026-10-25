using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;

namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = AccessRoles.Admin), Route("api/monitoring-rules/{ruleId:guid}/response-policies")]
public sealed class ResponsePoliciesController(OperationalResponsePolicies policies) : ControllerBase
{
    [HttpGet]
    public Task<List<ResponsePolicyView>> List(Guid ruleId, CancellationToken ct) => policies.List(ruleId, ct);

    [HttpPut("{priority}")]
    public Task<ResponsePolicyView> Save(Guid ruleId, string priority, ResponsePolicySaveRequest request, CancellationToken ct)
        => policies.Save(ruleId, priority, request, ct);

    [HttpDelete("{priority}"), ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid ruleId, string priority, ResponsePolicyDeleteRequest request, CancellationToken ct)
    {
        await policies.Delete(ruleId, priority, request, ct);
        return NoContent();
    }
}
