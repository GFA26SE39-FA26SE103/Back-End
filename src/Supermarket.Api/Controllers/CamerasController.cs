using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;
using Supermarket.Domain;
namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = "ADMIN"), Route("api/cameras")]
public sealed class CamerasController(CameraSetup setup) : ControllerBase
{
    [HttpGet("/api/floors/{id:guid}/cameras")] public Task<List<Camera>> List(Guid id, CancellationToken ct) => setup.List(id, ct);
    [HttpPost("/api/floors/{id:guid}/cameras")]
    public async Task<IActionResult> Create(Guid id, CameraRequest r, CancellationToken ct)
    {
        var x = await setup.Save(null, id, r, ct);
        return Created($"/api/cameras/{x.CameraId}", x);
    }
    [HttpGet("{id:guid}")] public Task<Camera> Get(Guid id, CancellationToken ct) => setup.Get(id, ct);
    [HttpPatch("{id:guid}")] public Task<Camera> Update(Guid id, CameraRequest r, CancellationToken ct) => setup.Save(id, null, r, ct);
    [HttpGet("{id:guid}/connection")] public Task<ConnectionView> Connection(Guid id, CancellationToken ct) => setup.Connection(id, ct);
    [HttpPut("{id:guid}/connection")] public Task<ConnectionView> Configure(Guid id, ConnectionRequest r, CancellationToken ct) => setup.Configure(id, r, ct);
    [HttpPost("{id:guid}/connection/test")] public Task<ConnectionView> Test(Guid id, CancellationToken ct) => setup.Test(id, ct);
    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Preview(Guid id, CancellationToken ct)
    {
        var frame = await setup.Preview(id, ct);
        Response.Headers.CacheControl = "no-store";
        return File(frame.Bytes, frame.ContentType);
    }
    [HttpPost("{id:guid}/connection/enable")] public Task<ConnectionView> Enable(Guid id, CancellationToken ct) => setup.Enable(id, true, ct);
    [HttpPost("{id:guid}/connection/disable")] public Task<ConnectionView> Disable(Guid id, CancellationToken ct) => setup.Enable(id, false, ct);
    [HttpGet("{id:guid}/zones")] public Task<List<MappingView>> Zones(Guid id, CancellationToken ct) => setup.Mappings(id, ct);
    [HttpPut("{id:guid}/zones/{zoneId:guid}")] public Task<MappingView> Map(Guid id, Guid zoneId, MappingRequest r, CancellationToken ct) => setup.Map(id, zoneId, r, ct);
    [HttpDelete("{id:guid}/zones/{zoneId:guid}")]
    public async Task<IActionResult> Unmap(Guid id, Guid zoneId, CancellationToken ct)
    {
        await setup.Unmap(id, zoneId, ct);
        return NoContent();
    }
}
