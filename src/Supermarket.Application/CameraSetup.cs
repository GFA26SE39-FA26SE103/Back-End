using System.Text.Json;
using Supermarket.Domain;
namespace Supermarket.Application;

public sealed class CameraSetup(ISetupStore store, ICurrentUser current, ICameraStream streams, ICredentialProtector secrets, IClock clock)
{
    public async Task<Camera> Get(Guid id, CancellationToken ct)
    {
        UseCase.Viewer(current);
        return UseCase.Found(await store.Find<Camera>(id, ct));
    }
    public async Task<List<Camera>> List(Guid floorId, CancellationToken ct)
    {
        UseCase.Viewer(current);
        UseCase.Found(await store.Find<Floor>(floorId, ct));
        return await store.List<Camera>(c => c.FloorId == floorId, ct);
    }
    public Task<Camera> Save(Guid? id, Guid? floorId, CameraRequest r, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            var x = id is null ? new Camera { CameraId = Guid.NewGuid(), FloorId = floorId!.Value } : UseCase.Found(await store.Find<Camera>(id.Value, ct));
            UseCase.Found(await store.Find<Floor>(x.FloorId, ct));
            x.Code = Rules.Text(r.Code, 50, "Code");
            UseCase.Unique((await store.List<Camera>(c => c.Code == x.Code && c.CameraId != x.CameraId, ct)).Count > 0);
            x.Name = Rules.Text(r.Name, 100, "Name");
            Rules.Optional(r.Manufacturer, 100, "Manufacturer");
            Rules.Optional(r.Model, 100, "Model");
            Rules.Optional(r.SerialNumber, 150, "SerialNumber");
            x.Manufacturer = r.Manufacturer;
            x.Model = r.Model;
            x.SerialNumber = r.SerialNumber;
            x.InstalledAt = Utc(r.InstalledAt);
            x.WarrantyExpiresAt = Utc(r.WarrantyExpiresAt);
            x.MapX = r.MapX;
            x.MapY = r.MapY;
            x.MapRotationDeg = r.MapRotationDeg;
            x.Status = r.Status;
            Rules.Camera(x);
            if (x.Status != "ACTIVE")
            {
                var connection = (await store.List<CameraConnection>(c => c.CameraId == x.CameraId, ct)).SingleOrDefault();
                if (connection is not null)
                {
                    connection.IsEnabled = false;
                    await store.Update(connection, ct);
                }
                x.HealthStatus = "UNKNOWN";
            }
            if (id is null)
                await store.Add(x, ct);
            else
                await store.Update(x, ct);
            return x;
        }, ct);
    }
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    public async Task<ConnectionView> Connection(Guid cameraId, CancellationToken ct)
    {
        // Connection details include the stream URI; keep them Admin-only.
        UseCase.Admin(current);
        await Get(cameraId, ct);
        return View(await LoadConnection(cameraId, ct));
    }
    private async Task<CameraConnection> LoadConnection(Guid id, CancellationToken ct) => UseCase.Found((await store.List<CameraConnection>(c => c.CameraId == id, ct)).SingleOrDefault());
    public Task<ConnectionView> Configure(Guid id, ConnectionRequest r, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            var camera = await Get(id, ct);
            foreach (var mapping in await store.List<CameraZoneMapping>(m => m.CameraId == id && m.Status == "ACTIVE", ct))
                if ((await store.List<MonitoringConfiguration>(m => m.ZoneId == mapping.ZoneId && m.Status == "ACTIVE", ct)).Count > 0)
                    throw new ApplicationException("MONITORING_ACTIVE", "Deactivate monitoring before replacing the camera source.");
            var x = (await store.List<CameraConnection>(c => c.CameraId == id, ct)).SingleOrDefault();
            var isNew = x is null;
            x ??= new CameraConnection { ConnectionId = Guid.NewGuid(), CameraId = id };
            x.SourceType = r.SourceType;
            x.Protocol = r.Protocol;
            x.StreamUri = r.StreamUri;
            x.SnapshotUri = r.SnapshotUri;
            Rules.Optional(r.Username, 150, "Username");
            Rules.Optional(r.Password, 128, "Password");
            // Each PUT replaces credentials too. Credentials are recoverably encrypted, never returned.
            x.Username = r.Username;
            x.CredentialSecretRef = string.IsNullOrEmpty(r.Password) ? null : secrets.Protect(r.Password);
            Rules.Optional(x.CredentialSecretRef, 500, "Encrypted credential");
            Rules.Connection(x);
            x.IsEnabled = false;
            x.LastTestedAt = null;
            x.LastTestResult = null;
            x.LastTestMessage = null;
            camera.HealthStatus = "UNKNOWN";
            await store.Update(camera, ct);
            if (isNew)
                await store.Add(x, ct);
            else
                await store.Update(x, ct);
            return View(x);
        }, ct);
    }
    public async Task<ConnectionView> Test(Guid id, CancellationToken ct)
    {
        UseCase.Admin(current);
        await Get(id, ct);
        var snapshot = await LoadConnection(id, ct);
        var result = await streams.Test(snapshot, ct);
        return await store.Transaction(async () =>
        {
            var connection = await LoadConnection(id, ct);
            CheckVersion(snapshot, connection);
            connection.LastTestedAt = clock.UtcNow;
            connection.LastTestResult = result.Success ? "SUCCESS" : "FAILED";
            connection.LastTestMessage = result.Code;
            await store.Update(connection, ct);
            return View(connection);
        }, ct);
    }
    public async Task<PreviewFrame> Preview(Guid id, CancellationToken ct)
    {
        await Get(id, ct);
        var connection = await LoadConnection(id, ct);
        if (connection.LastTestResult != "SUCCESS")
            throw new ApplicationException("CONNECTION_NOT_READY", "Test the current connection before preview.");
        return await streams.Preview(connection, ct);
    }
    public Task<ConnectionView> Enable(Guid id, bool enabled, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            var camera = await Get(id, ct);
            var connection = await LoadConnection(id, ct);
            if (enabled)
                Rules.Enable(camera, connection);
            connection.IsEnabled = enabled;
            await store.Update(connection, ct);
            if (!enabled)
            {
                camera.HealthStatus = "UNKNOWN";
                await store.Update(camera, ct);
            }
            return View(connection);
        }, ct);
    }
    public async Task<List<MappingView>> Mappings(Guid id, CancellationToken ct)
    {
        await Get(id, ct);
        return (await store.List<CameraZoneMapping>(m => m.CameraId == id, ct)).Select(Mapping).ToList();
    }
    public Task<MappingView> Map(Guid id, Guid zoneId, MappingRequest r, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            var camera = await Get(id, ct);
            var zone = UseCase.Found(await store.Find<Zone>(zoneId, ct));
            Rules.SameFloor(camera, zone);
            Rules.Polygon(r.RoiPolygon);
            Rules.Status(r.Status, "ACTIVE", "INACTIVE");
            var x = (await store.List<CameraZoneMapping>(m => m.CameraId == id && m.ZoneId == zoneId, ct)).SingleOrDefault();
            var isNew = x is null;
            if ((await store.List<MonitoringConfiguration>(m => m.ZoneId == zoneId && m.Status == "ACTIVE", ct)).Count > 0)
                throw new ApplicationException("MONITORING_ACTIVE", "Deactivate monitoring before changing camera mapping or ROI.");
            x ??= new CameraZoneMapping { CameraZoneId = Guid.NewGuid(), CameraId = id, ZoneId = zoneId };
            x.RoiPolygon = JsonSerializer.Serialize(r.RoiPolygon);
            x.Status = r.Status;
            if (isNew)
                await store.Add(x, ct);
            else
                await store.Update(x, ct);
            return Mapping(x);
        }, ct);
    }
    public Task<bool> Unmap(Guid id, Guid zoneId, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            await Get(id, ct);
            var x = UseCase.Found((await store.List<CameraZoneMapping>(m => m.CameraId == id && m.ZoneId == zoneId, ct)).SingleOrDefault());
            if ((await store.List<MonitoringConfiguration>(m => m.ZoneId == zoneId && m.Status == "ACTIVE", ct)).Count > 0)
                throw new ApplicationException("MONITORING_ACTIVE", "Deactivate monitoring before removing a mapping.");
            await store.Remove(x, ct);
            return true;
        }, ct);
    }
    internal static void CheckVersion(CameraConnection before, CameraConnection after)
    {
        if (before.UpdatedAt != after.UpdatedAt)
            throw new ApplicationException("CONNECTION_CHANGED", "Connection changed during the probe; retry with the current configuration.");
    }
    public static ConnectionView View(CameraConnection c) => new(c.ConnectionId, c.CameraId, c.SourceType, c.Protocol, c.StreamUri, c.SnapshotUri, c.CredentialSecretRef is not null, c.IsEnabled, c.LastTestedAt, c.LastTestResult, c.LastTestMessage, c.UpdatedAt);
    private static MappingView Mapping(CameraZoneMapping m) => new(m.CameraZoneId, m.CameraId, m.ZoneId, JsonSerializer.Deserialize<Point[]>(m.RoiPolygon)!, m.Status);
}
