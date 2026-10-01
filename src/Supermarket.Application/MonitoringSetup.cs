using Supermarket.Domain;
namespace Supermarket.Application;

public sealed class MonitoringSetup(ISetupStore store, ICurrentUser current)
{
    public async Task<MonitoringConfiguration> Get(Guid zoneId, CancellationToken ct)
    {
        UseCase.Admin(current);
        UseCase.Found(await store.Find<Zone>(zoneId, ct));
        return UseCase.Found((await store.List<MonitoringConfiguration>(m => m.ZoneId == zoneId, ct)).SingleOrDefault());
    }
    public Task<MonitoringConfiguration> Save(Guid zoneId, MonitoringRequest r, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            UseCase.Found(await store.Find<Zone>(zoneId, ct));
            Rules.Confidence(r.ConfidenceThreshold);
            var x = (await store.List<MonitoringConfiguration>(m => m.ZoneId == zoneId, ct)).SingleOrDefault();
            var isNew = x is null;
            x ??= new MonitoringConfiguration { ConfigId = Guid.NewGuid(), ZoneId = zoneId, CreatedByUserId = current.UserId };
            x.Name = Rules.Text(r.Name, 100, "Name");
            x.ConfidenceThreshold = r.ConfidenceThreshold;
            x.Status = "DRAFT";
            if (isNew)
                await store.Add(x, ct);
            else
                await store.Update(x, ct);
            return x;
        }, ct);
    }
    public Task<MonitoringConfiguration> Activate(Guid zoneId, bool active, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            var x = await Get(zoneId, ct);
            if (active)
            {
                var zone = UseCase.Found(await store.Find<Zone>(zoneId, ct));
                if (zone.Status != "ACTIVE")
                    throw new ApplicationException("ZONE_INACTIVE", "An active zone is required.");
                var mappings = await store.List<CameraZoneMapping>(m => m.ZoneId == zoneId && m.Status == "ACTIVE", ct);
                bool ready = false;
                foreach (var m in mappings)
                {
                    var camera = UseCase.Found(await store.Find<Camera>(m.CameraId, ct));
                    var connection = (await store.List<CameraConnection>(c => c.CameraId == m.CameraId, ct)).SingleOrDefault();
                    if (camera.Status == "ACTIVE" && connection is { IsEnabled: true, LastTestResult: "SUCCESS" })
                        ready = true;
                }
                if (!ready)
                    throw new ApplicationException("MONITORING_NOT_READY", "Map an active camera with an enabled, tested connection before activation.");
            }
            x.Status = active ? "ACTIVE" : "INACTIVE";
            await store.Update(x, ct);
            return x;
        }, ct);
    }
}
