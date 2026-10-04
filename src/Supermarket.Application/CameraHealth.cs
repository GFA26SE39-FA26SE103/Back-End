using Supermarket.Domain;
namespace Supermarket.Application;

public sealed class CameraHealth(ISetupStore store, ICurrentUser current, ICameraStream streams, IClock clock, CameraHealthRuntimeState runtime)
{
    public async Task<CameraHealthView> Get(Guid id, CancellationToken ct)
    {
        UseCase.Admin(current);
        var camera = UseCase.Found(await store.Find<Camera>(id, ct));
        var connection = (await store.List<CameraConnection>(c => c.CameraId == id, ct)).SingleOrDefault();
        var unresolved = await store.List<CameraHealthEvent>(e => e.CameraId == id && e.Status != "RESOLVED", ct);
        return runtime.Snapshot(camera, unresolved.Select(e => e.EventType), connection?.IsEnabled == true);
    }
    public Task<List<CameraHealthEvent>> Events(Guid? cameraId, string? status, CancellationToken ct)
    {
        UseCase.Admin(current);
        if (status is not null)
            Rules.Status(status, "OPEN", "INVESTIGATING", "RESOLVED");
        return store.List<CameraHealthEvent>(e => (cameraId == null || e.CameraId == cameraId) && (status == null || e.Status == status), ct);
    }
    public async Task<CameraHealthView> Check(Guid cameraId, CancellationToken ct)
    {
        UseCase.Admin(current);
        return await CheckInternal(cameraId, ct);
    }
    public Task<CameraHealthView> CheckInternal(Guid cameraId, CancellationToken ct)
    {
        return runtime.Serialize(cameraId, async () =>
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
                var observedAt = clock.UtcNow;
                if (probe.Success)
                    camera.LastSeenAt = observedAt;
                var unresolved = await store.List<CameraHealthEvent>(e => e.CameraId == cameraId && e.Status != "RESOLVED", ct);
                var transition = runtime.Observe(camera, probe, unresolved.Select(e => e.EventType), observedAt);
                foreach (var eventType in transition.OpenIssues)
                    await store.Add(new CameraHealthEvent
                    {
                        HealthEventId = Guid.NewGuid(), CameraId = cameraId, EventType = eventType, DetectedAt = observedAt
                    }, ct);
                foreach (var health in unresolved.Where(e => transition.ResolvedIssues.Contains(e.EventType, StringComparer.Ordinal)))
                {
                    health.Status = "RESOLVED";
                    health.ResolvedAt = observedAt;
                    health.ResolutionNote = "Health monitor confirmed recovery.";
                    await store.Update(health, ct);
                }
                await store.Update(camera, ct);
                return transition.View;
            }, ct);
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
            // Recovery normally auto-resolves first; attaching the Admin's note remains idempotent.
            if (x.Status != "RESOLVED")
            {
                var recovered = runtime.IsRecovered(x.CameraId, x.EventType, camera.HealthStatus);
                if (!recovered)
                    throw new ApplicationException("HEALTH_ISSUE_NOT_RECOVERED", "Run a successful health check before resolving this issue.", 422);
                Rules.Resolve(x, recovered: true);
            }
            x.Status = "RESOLVED";
            x.ResolvedAt ??= clock.UtcNow;
            x.ResolutionNote = Rules.Text(r.ResolutionNote, 1000, "ResolutionNote");
            await store.Update(x, ct);
            return x;
        }, ct);
    }
}
