using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;
namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = "ADMIN"), Route("api/setup")]
public sealed class SetupController(SetupOverview overview) : ControllerBase
{
    [HttpGet("overview")]
    public Task<SetupOverviewView> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return overview.Get(ct);
    }
}
