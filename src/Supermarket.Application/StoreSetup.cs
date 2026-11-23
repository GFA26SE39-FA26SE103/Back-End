using System.Text.Json;
using Supermarket.Domain;
using Store = Supermarket.Domain.Supermarket;
namespace Supermarket.Application;

public sealed class StoreSetup(ISetupStore store, ICurrentUser current, CheckoutSourceGuard? checkoutGuard=null)
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
    public Task<Floor> UpdateFloorDetails(Guid id, FloorDetailsRequest request, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            var floor = UseCase.Found(await store.Find<Floor>(id, ct));
            UseCase.Unique((await store.List<Floor>(f => f.SupermarketId == floor.SupermarketId
                && f.FloorNumber == request.FloorNumber && f.FloorId != id, ct)).Count > 0);
            var name = Rules.Text(request.Name, 100, "Name");
            floor.FloorNumber = request.FloorNumber;
            floor.Name = name;
            // Read and retain the current map metadata; the editor never submits an old asset URL/dimensions.
            await store.Update(floor, ct);
            return floor;
        }, ct);
    }
    public Task<Floor> DeleteFloor(Guid id, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            var floor = UseCase.Found(await store.Find<Floor>(id, ct));
            var zones = await store.Count<Zone>(zone => zone.FloorId == id, ct);
            var cameras = await store.Count<Camera>(camera => camera.FloorId == id, ct);
            if (zones > 0 || cameras > 0)
                throw new ApplicationException("FLOOR_IN_USE",
                    $"Cannot delete {floor.Name}. This floor still has {zones} zone(s) and {cameras} camera(s).");

            await store.Remove(floor, ct);
            return floor;
        }, ct);
    }
    public async Task<ZoneView> Zone(Guid id, CancellationToken ct) => View(await Get<Zone>(id, ct));
    public Task<ZoneView> SaveZone(Guid? id, Guid? floorId, ZoneRequest r, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            var x = id is null ? new Zone { ZoneId = Guid.NewGuid(), FloorId = floorId!.Value } : UseCase.Found(await store.Find<Zone>(id.Value, ct));
            UseCase.Found(await store.Find<Floor>(x.FloorId, ct));
            if(id is not null && checkoutGuard is not null) await checkoutGuard.EnsureZoneTypeChange(x,r.ZoneType,ct);
            x.Code = Rules.Text(r.Code, 50, "Code");
            UseCase.Unique((await store.List<Zone>(z => z.FloorId == x.FloorId && z.Code == x.Code && z.ZoneId != x.ZoneId, ct)).Count > 0);
            Rules.Rectangle(r.MapPolygon);
            Rules.Status(r.Status, "ACTIVE", "INACTIVE");
            Rules.Optional(r.ZoneType, 50, "ZoneType");
            Rules.ZoneColor(r.ColorHex);
            if (r.AreaM2Specified)
                Rules.Require(r.AreaM2 is null || MonitoringPolicy.ValidDensityArea(r.AreaM2), "INVALID_ZONE_AREA",
                    "Zone area must be between 0.01 and 9999999999.99 m² with at most two decimal places.");
            if (r.AreaM2Specified && r.AreaM2 != x.AreaM2)
            {
                var active = (await store.List<MonitoringConfiguration>(m => m.ZoneId == x.ZoneId && m.Status == "ACTIVE", ct)).SingleOrDefault();
                if (active is not null && (await store.List<MonitoringRule>(rule => rule.ConfigId == active.ConfigId
                        && rule.Enabled && rule.ThresholdUnit == MonitoringPolicy.DensityUnit, ct)).Count > 0)
                    throw new ApplicationException("MONITORING_ACTIVE", "Deactivate crowd density monitoring before changing the zone area.");
                x.AreaM2 = r.AreaM2;
            }
            x.Name = Rules.Text(r.Name, 100, "Name");
            x.ZoneType = r.ZoneType;
            x.ColorHex = r.ColorHex?.ToUpperInvariant();
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
    public async Task<ZoneUsageView> ZoneUsage(Guid id, CancellationToken ct)
    {
        UseCase.Admin(current);
        UseCase.Found(await store.Find<Zone>(id, ct));
        var dependencies = await ZoneDependencies(id, ct);
        ZoneMonitoringUsageView? monitoring = null;
        if (dependencies.Any(x => x.Kind == "MONITORING"))
        {
            var config = (await store.List<MonitoringConfiguration>(x => x.ZoneId == id, ct)).SingleOrDefault();
            if (config is not null)
            {
                var rules = (await store.List<MonitoringRule>(x => x.ConfigId == config.ConfigId, ct)).ToArray();
                var eventTypes = (await store.List<OperationalEventType>(ct: ct)).ToDictionary(x => x.EventTypeId);
                var view = MonitoringSetup.ConfigurationView(config, rules, eventTypes);
                monitoring = new(view.Name, view.Status,
                    view.Rules.Select(x => new ZoneRuleUsageView(x.RuleId,
                        x.EventCode == "CONGESTION" && x.ThresholdUnit == MonitoringPolicy.DensityUnit ? "Crowd Density" : x.EventName,
                        x.Enabled)).ToArray());
            }
        }
        string[] staffNames = [];
        if (dependencies.Any(x => x.Kind == "SHIFT"))
        {
            var assignments = await store.List<StaffZoneAssignment>(x => x.ZoneId == id, ct);
            var recentIds = assignments.OrderByDescending(x => x.AssignedAt).Select(x => x.ShiftAssignmentId).Distinct().Take(10).ToArray();
            var shifts = await store.List<ShiftAssignment>(x => recentIds.Contains(x.ShiftAssignmentId), ct);
            var staffIds = shifts.Select(x => x.UserId).Distinct().ToArray();
            var staff = await store.List<UserAccount>(x => staffIds.Contains(x.UserId), ct);
            staffNames = staff.Select(x => x.FullName).Distinct().Order().Take(3).ToArray();
        }
        return new(dependencies, monitoring, staffNames);
    }

    public Task DeleteZone(Guid id, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            var zone = UseCase.Found(await store.Find<Zone>(id, ct));
            var dependencies = await ZoneDependencies(id, ct);
            if (dependencies.Length > 0)
            {
                var usage = string.Join(", ", dependencies.Select(x => $"{x.Label.ToLowerInvariant()} ({x.Count})"));
                throw new ApplicationException("ZONE_IN_USE",
                    $"Cannot delete {zone.Name}. Still used by {usage}. You can still edit and save the zone.");
            }
            foreach (var adjacency in await store.List<ZoneAdjacency>(x => x.ZoneId == id || x.AdjacentZoneId == id, ct))
                await store.Remove(adjacency, ct);
            await store.Remove(zone, ct);
            return true;
        }, ct);
    }
    private async Task<ZoneDependencyView[]> ZoneDependencies(Guid id, CancellationToken ct)
    {
        ZoneDependencyView[] references =
        [
            new("MONITORING", "Monitoring configurations", await store.Count<MonitoringConfiguration>(x => x.ZoneId == id, ct)),
            new("SHIFT", "Staff zone assignments", await store.Count<StaffZoneAssignment>(x => x.ZoneId == id, ct)),
            new("CAMERA", "Camera coverage mappings", await store.Count<CameraZoneMapping>(x => x.ZoneId == id, ct)),
            new("CHECKOUT", "Checkout counters", await store.Count<CheckoutCounter>(x => x.ZoneId == id, ct)),
            new("EVENT", "Operational events", await store.Count<OperationalEvent>(x => x.ZoneId == id, ct)),
            new("INCIDENT", "Incidents", await store.Count<Incident>(x => x.ZoneId == id, ct)),
            new("REPORT", "Staff reports", await store.Count<StaffReport>(x => x.ZoneId == id, ct)),
            new("TASK", "Tasks", await store.Count<WorkTask>(x => x.ZoneId == id, ct)),
            new("RECOMMENDATION", "Recommendations", await store.Count<Recommendation>(x => x.ZoneId == id, ct))
        ];
        return references.Where(x => x.Count > 0).ToArray();
    }
    public static ZoneView View(Zone x) => new(x.ZoneId, x.FloorId, x.Code, x.Name, x.ZoneType, JsonSerializer.Deserialize<Point[]>(x.MapPolygon)!, x.ColorHex, x.MinStaff, x.Status, x.UpdatedAt, x.AreaM2);
    private Task Save<T>(T x, Guid? id, CancellationToken ct) where T : Entity, new() => id is null ? store.Add(x, ct) : store.Update(x, ct);
}
