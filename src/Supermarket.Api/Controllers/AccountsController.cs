using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Supermarket.Application;
namespace Supermarket.Api.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(Accounts accounts) : ControllerBase
{
    [AllowAnonymous, HttpPost("login"), EnableRateLimiting("login")]
    public Task<LoginResponse> Login(LoginRequest request, CancellationToken ct) => accounts.Login(request, ct);
    [Authorize, HttpGet("me")]
    public Task<UserView> Me(CancellationToken ct) => accounts.Me(ct);
}
[ApiController, Authorize(Roles = "ADMIN"), Route("api/users")]
public sealed class UsersController(Accounts accounts) : ControllerBase
{
    [HttpGet] public Task<List<UserView>> List(CancellationToken ct) => accounts.List(ct);
    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest r, CancellationToken ct)
    {
        var user = await accounts.Create(r, ct);
        return Created($"/api/users/{user.UserId}", user);
    }
    [HttpPatch("{id:guid}")] public Task<UserView> Update(Guid id, UpdateUserRequest r, CancellationToken ct) => accounts.Update(id, r, ct);
    [HttpPost("{id:guid}/disable")] public Task<UserView> Disable(Guid id, CancellationToken ct) => accounts.Status(id, false, ct);
    [HttpPost("{id:guid}/enable")] public Task<UserView> Enable(Guid id, CancellationToken ct) => accounts.Status(id, true, ct);
    [HttpGet("/api/roles")] public Task<List<Supermarket.Domain.Role>> Roles(CancellationToken ct) => accounts.Roles(ct);
}
