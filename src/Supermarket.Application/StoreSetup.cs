using System.Text.Json;
using Supermarket.Domain;
using Store = Supermarket.Domain.Supermarket;
namespace Supermarket.Application;

public sealed class StoreSetup(ISetupStore store, ICurrentUser current)
{
    public Task<List<Store>> Stores(CancellationToken ct)
    {
        UseCase.Viewer(current);
        return store.List<Store>(ct: ct);
    }
    public async Task<T> Get<T>(Guid id, CancellationToken ct) where T : Entity, new()
    {
        UseCase.Viewer(current);
        return UseCase.Found(await store.Find<T>(id, ct));
    }
    public Task<Store> SaveStore(Guid? id, StoreRequest r, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            var all = await store.List<Store>(ct: ct);
            if (id is null && all.Count > 0)
                throw new ApplicationException("SINGLE_BRANCH", "Only one supermarket is supported in this increment.");
            var x = id is null ? new Store { SupermarketId = Guid.NewGuid() } : UseCase.Found(await store.Find<Store>(id.Value, ct));
            x.Code = Rules.Text(r.Code, 50, "Code");
            x.Name = Rules.Text(r.Name, 150, "Name");
            Rules.Optional(r.Address, 500, "Address");
            x.Address = r.Address;
            Rules.Status(r.Status, "ACTIVE", "INACTIVE");
            x.Status = r.Status;
            await Save(x, id, ct);
            return x;
        }, ct);
    }
    public async Task<List<Floor>> Floors(Guid storeId, CancellationToken ct)
    {
        await Get<Store>(storeId, ct);
        return await store.List<Floor>(f => f.SupermarketId == storeId, ct);
    }
    public Task<Floor> SaveFloor(Guid? id, Guid? storeId, FloorRequest r, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            var x = id is null ? new Floor { FloorId = Guid.NewGuid(), SupermarketId = storeId!.Value } : UseCase.Found(await store.Find<Floor>(id.Value, ct));
            UseCase.Found(await store.Find<Store>(x.SupermarketId, ct));
            UseCase.Unique((await store.List<Floor>(f => f.SupermarketId == x.SupermarketId && f.FloorNumber == r.FloorNumber && f.FloorId != x.FloorId, ct)).Count > 0);
            Rules.MapSize(r.MapWidth, r.MapHeight);
            x.FloorNumber = r.FloorNumber;
            x.Name = Rules.Text(r.Name, 100, "Name");
            Rules.Optional(r.MapAssetUrl, 1000, "MapAssetUrl");
            if (r.MapAssetUrl is not null)
                Rules.Require(Uri.TryCreate(r.MapAssetUrl, UriKind.Absolute, out var mapUri) && mapUri.Scheme is "http" or "https", "INVALID_MAP_URL", "Floor maps must reference an HTTP(S) asset.");
            x.MapAssetUrl = r.MapAssetUrl;
            x.MapWidth = r.MapWidth;
            x.MapHeight = r.MapHeight;
            await Save(x, id, ct);
            return x;
        }, ct);
    }
    public async Task<List<ZoneView>> Zones(Guid floorId, CancellationToken ct)
    {
        await Get<Floor>(floorId, ct);
        return (await store.List<Zone>(z => z.FloorId == floorId, ct)).Select(View).ToList();
    }
    public async Task<ZoneView> Zone(Guid id, CancellationToken ct) => View(await Get<Zone>(id, ct));
    public Task<ZoneView> SaveZone(Guid? id, Guid? floorId, ZoneRequest r, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            var x = id is null ? new Zone { ZoneId = Guid.NewGuid(), FloorId = floorId!.Value } : UseCase.Found(await store.Find<Zone>(id.Value, ct));
            UseCase.Found(await store.Find<Floor>(x.FloorId, ct));
            if (id is not null && x.AreaM2 != r.AreaM2
                && (await store.List<MonitoringConfiguration>(m => m.ZoneId == x.ZoneId && m.Status == "ACTIVE", ct)).Count > 0)
                throw new ApplicationException("MONITORING_ACTIVE", "Deactivate monitoring before changing the physical zone area used by its measurements.");
            x.Code = Rules.Text(r.Code, 50, "Code");
            UseCase.Unique((await store.List<Zone>(z => z.FloorId == x.FloorId && z.Code == x.Code && z.ZoneId != x.ZoneId, ct)).Count > 0);
            Rules.Polygon(r.MapPolygon);
            Rules.Status(r.Status, "ACTIVE", "INACTIVE");
            Rules.Optional(r.ZoneType, 50, "ZoneType");
            Rules.ZoneColor(r.ColorHex);
            Rules.ZoneArea(r.AreaM2);
            x.Name = Rules.Text(r.Name, 100, "Name");
            x.ZoneType = r.ZoneType;
            x.ColorHex = r.ColorHex?.ToUpperInvariant();
            x.AreaM2 = r.AreaM2;
            x.MapPolygon = JsonSerializer.Serialize(r.MapPolygon);
            x.Status = r.Status;
            if (x.Status != "ACTIVE")
            {
                var active = await store.List<MonitoringConfiguration>(m => m.ZoneId == x.ZoneId && m.Status == "ACTIVE", ct);
                if (active.Count > 0)
                    throw new ApplicationException("MONITORING_ACTIVE", "Deactivate zone monitoring before disabling the zone.");
            }
            await Save(x, id, ct);
            return View(x);
        }, ct);
    }
    public static ZoneView View(Zone x) => new(x.ZoneId, x.FloorId, x.Code, x.Name, x.ZoneType, JsonSerializer.Deserialize<Point[]>(x.MapPolygon)!, x.ColorHex, x.AreaM2, x.Status, x.UpdatedAt);
    private Task Save<T>(T x, Guid? id, CancellationToken ct) where T : Entity, new() => id is null ? store.Add(x, ct) : store.Update(x, ct);
}
