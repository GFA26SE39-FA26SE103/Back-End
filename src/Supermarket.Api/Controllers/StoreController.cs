using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Supermarket.Application;
using Supermarket.Domain;
using Store = Supermarket.Domain.Supermarket;
namespace Supermarket.Api.Controllers;

[ApiController, Authorize(Roles = "ADMIN"), Route("api/supermarkets")]
public sealed class StoreController(StoreSetup setup) : ControllerBase
{
    [HttpGet] public Task<List<Store>> List(CancellationToken ct) => setup.Stores(ct);
    [HttpGet("{id:guid}")] public Task<Store> Get(Guid id, CancellationToken ct) => setup.Get<Store>(id, ct);
    [HttpPost]
    public async Task<IActionResult> Create(StoreRequest r, CancellationToken ct)
    {
        var x = await setup.SaveStore(null, r, ct);
        return Created($"/api/supermarkets/{x.SupermarketId}", x);
    }
    [HttpPatch("{id:guid}")] public Task<Store> Update(Guid id, StoreRequest r, CancellationToken ct) => setup.SaveStore(id, r, ct);
    [HttpGet("{id:guid}/floors")] public Task<List<Floor>> Floors(Guid id, CancellationToken ct) => setup.Floors(id, ct);
    [HttpPost("{id:guid}/floors")]
    public async Task<IActionResult> CreateFloor(Guid id, FloorRequest r, CancellationToken ct)
    {
        var x = await setup.SaveFloor(null, id, r, ct);
        return Created($"/api/floors/{x.FloorId}", x);
    }
    [HttpGet("/api/floors/{id:guid}")] public Task<Floor> Floor(Guid id, CancellationToken ct) => setup.Get<Floor>(id, ct);
    [HttpPatch("/api/floors/{id:guid}")] public Task<Floor> UpdateFloor(Guid id, FloorRequest r, CancellationToken ct) => setup.SaveFloor(id, null, r, ct);
    [HttpGet("/api/floors/{id:guid}/zones")] public Task<List<ZoneView>> Zones(Guid id, CancellationToken ct) => setup.Zones(id, ct);
    [HttpPost("/api/floors/{id:guid}/zones")]
    public async Task<IActionResult> CreateZone(Guid id, ZoneRequest r, CancellationToken ct)
    {
        var x = await setup.SaveZone(null, id, r, ct);
        return Created($"/api/zones/{x.ZoneId}", x);
    }
    [HttpGet("/api/zones/{id:guid}")] public Task<ZoneView> Zone(Guid id, CancellationToken ct) => setup.Zone(id, ct);
    [HttpPatch("/api/zones/{id:guid}")] public Task<ZoneView> UpdateZone(Guid id, ZoneRequest r, CancellationToken ct) => setup.SaveZone(id, null, r, ct);
}
