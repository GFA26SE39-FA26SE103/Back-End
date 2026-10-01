using Supermarket.Domain;
namespace Supermarket.Application;

public sealed class CameraHealth(ISetupStore store, ICurrentUser current, ICameraStream streams, IClock clock)
{
    public async Task<Camera> Get(Guid id, CancellationToken ct)
    {
        UseCase.Admin(current);
        return UseCase.Found(await store.Find<Camera>(id, ct));
    }
    public Task<List<CameraHealthEvent>> Events(Guid? cameraId, string? status, CancellationToken ct)
    {
        UseCase.Admin(current);
        if (status is not null)
            Rules.Status(status, "OPEN", "INVESTIGATING", "RESOLVED");
        return store.List<CameraHealthEvent>(e => (cameraId == null || e.CameraId == cameraId) && (status == null || e.Status == status), ct);
    }
    public async Task<Camera> Check(Guid cameraId, CancellationToken ct)
    {
        UseCase.Admin(current);
        return await CheckInternal(cameraId, ct);
    }
    public async Task<Camera> CheckInternal(Guid cameraId, CancellationToken ct)
    {
        var before = UseCase.Found((await store.List<CameraConnection>(c => c.CameraId == cameraId, ct)).SingleOrDefault());
        if (!before.IsEnabled)
            throw new ApplicationException("CONNECTION_DISABLED", "Enable the connection before checking operational health.");
        var probe = await streams.Test(before, ct);
        return await store.Transaction(async () =>
        {
            var connection = UseCase.Found((await store.List<CameraConnection>(c => c.CameraId == cameraId, ct)).SingleOrDefault());
            CameraSetup.CheckVersion(before, connection);
            var camera = UseCase.Found(await store.Find<Camera>(cameraId, ct));
            if (!connection.IsEnabled || camera.Status != "ACTIVE")
                throw new ApplicationException("CONNECTION_DISABLED", "Connection is no longer enabled.");
            camera.HealthStatus = probe.Success ? "ONLINE" : "OFFLINE";
            if (probe.Success)
                camera.LastSeenAt = clock.UtcNow;
            var unresolved = await store.List<CameraHealthEvent>(e => e.CameraId == cameraId && e.Status != "RESOLVED", ct);
            if (!probe.Success && unresolved.Count == 0)
                await store.Add(new CameraHealthEvent { HealthEventId = Guid.NewGuid(), CameraId = cameraId, EventType = "STREAM_UNAVAILABLE", DetectedAt = clock.UtcNow }, ct);
            if (probe.Success)
            foreach (var health in unresolved)
            {
                health.Status = "RESOLVED";
                health.ResolvedAt = clock.UtcNow;
                health.ResolutionNote = "Connection restored by health monitor.";
                await store.Update(health, ct);
            }
            await store.Update(camera, ct);
            return camera;
        }, ct);
    }
    public Task<CameraHealthEvent> Investigate(Guid id, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            var x = UseCase.Found(await store.Find<CameraHealthEvent>(id, ct));
            Rules.Investigate(x);
            x.Status = "INVESTIGATING";
            x.InvestigatedByUserId = current.UserId;
            x.InvestigatedAt = clock.UtcNow;
            await store.Update(x, ct);
            return x;
        }, ct);
    }
    public Task<CameraHealthEvent> Resolve(Guid id, ResolveRequest r, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            var x = UseCase.Found(await store.Find<CameraHealthEvent>(id, ct));
            var camera = UseCase.Found(await store.Find<Camera>(x.CameraId, ct));
            // Recovery can auto-resolve first; attaching the Admin's note remains idempotent.
            if (x.Status != "RESOLVED")
                Rules.Resolve(x, camera);
            if (camera.HealthStatus != "ONLINE")
                throw new ApplicationException("CAMERA_NOT_RESTORED", "Restore camera connectivity before resolution.");
            x.Status = "RESOLVED";
            x.ResolvedAt ??= clock.UtcNow;
            x.ResolutionNote = Rules.Text(r.ResolutionNote, 1000, "ResolutionNote");
            await store.Update(x, ct);
            return x;
        }, ct);
    }
}
