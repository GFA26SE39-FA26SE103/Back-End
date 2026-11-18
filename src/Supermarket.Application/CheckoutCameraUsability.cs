using Supermarket.Domain;

namespace Supermarket.Application;

public sealed class CheckoutCameraUsability(ISetupStore store, CameraHealthRuntimeState health) : ICheckoutCameraUsability
{
    public async Task<bool> Ready(Guid cameraId, CancellationToken ct)
    {
        var camera = await store.Find<Camera>(cameraId, ct);
        if (camera is null) return false;
        var connection = (await store.List<CameraConnection>(c => c.CameraId == cameraId, ct)).SingleOrDefault();
        var problems = await store.List<CameraHealthEvent>(e => e.CameraId == cameraId && e.Status != "RESOLVED", ct);
        return health.Snapshot(camera, problems.Select(p => p.EventType), connection?.IsEnabled == true).MonitoringReadiness == "READY";
    }
}
