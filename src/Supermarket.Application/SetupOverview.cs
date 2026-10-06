using Supermarket.Domain;
using Store = Supermarket.Domain.Supermarket;
namespace Supermarket.Application;

public sealed record SetupTotals(int FloorCount, int ZoneCount, int CameraCount, int ConfiguredZoneCount,
    int ActiveConfigurationCount, int ReadyToActivateCount, int OnlineCameraCount, int EnabledCameraCount, int UnresolvedHealthEventCount);
public sealed record SetupStep(string Code, string Name, int Completed, int Total, string Description);
public sealed record SetupConfiguration(Guid ConfigId, string Name, string Status, int RuleCount, int EnabledRuleCount);
public sealed record SetupZone(Guid ZoneId, string Code, string Name, string Status, SetupConfiguration? Configuration,
    bool SetupReady, bool CanActivate, MonitoringCameraView[] Cameras, MonitoringIssue[] Issues, string[] Warnings);
public sealed record SetupFloor(Guid FloorId, int FloorNumber, string Name, bool HasMap, SetupZone[] Zones);
public sealed record SetupCamera(Guid CameraId, Guid FloorId, string FloorName, string Code, string Name, string Status,
    string HealthStatus, string MonitoringReadiness, string ProcessingAvailability, string[] ActiveHealthIssues,
    DateTime? LastSeenAt, bool HasConnection, bool ConnectionValid, bool IsEnabled,
    string? SourceType, string? Protocol, string? LastTestResult, DateTime? LastTestedAt, MonitoringIssue[] Issues);
public sealed record SetupHealthEvent(Guid HealthEventId, Guid CameraId, string CameraCode, string EventType, string Status, DateTime DetectedAt);
public sealed record SetupOverviewView(DateTime GeneratedAt, bool HasDefaultStore, SetupTotals Totals,
    SetupStep[] Steps, SetupFloor[] Floors, SetupCamera[] Cameras, SetupHealthEvent[] HealthEvents);

public sealed class SetupOverview(ISetupStore store, ICurrentUser current, IClock clock, CameraHealthRuntimeState runtime)
{
    public Task<SetupOverviewView> Get(CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            var supermarket = (await store.List<Store>(ct: ct)).SingleOrDefault();
            List<Floor> floors = supermarket is null ? [] : await store.List<Floor>(f => f.SupermarketId == supermarket.SupermarketId, ct);
            var floorIds = floors.Select(f => f.FloorId).ToArray();
            // Sequential batched reads share one EF context and a consistent transaction. No probes or mutations.
            var zones = await store.List<Zone>(z => floorIds.Contains(z.FloorId), ct);
            var zoneIds = zones.Select(z => z.ZoneId).ToArray();
            var cameras = (await store.List<Camera>(c => floorIds.Contains(c.FloorId), ct)).ToDictionary(c => c.CameraId);
            var cameraIds = cameras.Keys.ToArray();
            var connections = (await store.List<CameraConnection>(c => cameraIds.Contains(c.CameraId), ct)).ToDictionary(c => c.CameraId);
            var mappings = await store.List<CameraZoneMapping>(m => zoneIds.Contains(m.ZoneId), ct);
            var configurations = (await store.List<MonitoringConfiguration>(c => zoneIds.Contains(c.ZoneId), ct)).ToDictionary(c => c.ZoneId);
            var configIds = configurations.Values.Select(c => c.ConfigId).ToArray();
            var rules = (await store.List<MonitoringRule>(r => configIds.Contains(r.ConfigId), ct)).ToLookup(r => r.ConfigId);
            var types = (await store.List<IncidentType>(ct: ct)).ToDictionary(t => t.IncidentTypeId);
            var events = await store.List<CameraHealthEvent>(e => cameraIds.Contains(e.CameraId) && e.Status != "RESOLVED", ct);
            var floorViews = floors.OrderBy(f => f.FloorNumber).Select(f => new SetupFloor(f.FloorId, f.FloorNumber, f.Name,
                !string.IsNullOrWhiteSpace(f.MapAssetUrl), zones.Where(z => z.FloorId == f.FloorId).OrderBy(z => z.Code).Select(z =>
                {
                    if (!configurations.TryGetValue(z.ZoneId, out var configuration))
                        return new SetupZone(z.ZoneId, z.Code, z.Name, z.Status, null, false, false,
                            MonitoringSetup.MappedCameras(z, mappings, cameras, connections),
                            [new("CONFIGURATION_MISSING", "Create an AI configuration and add incident rules.")], []);
                    var rows = rules[configuration.ConfigId].ToArray();
                    var review = MonitoringSetup.EvaluateReview(z, MonitoringSetup.ConfigurationView(configuration, rows, types), rows, types, mappings, cameras, connections);
                    return new SetupZone(z.ZoneId, z.Code, z.Name, z.Status,
                        new(configuration.ConfigId, configuration.Name, configuration.Status, rows.Length, rows.Count(r => r.Enabled)),
                        review.CanActivate, configuration.Status != "ACTIVE" && review.CanActivate, review.Cameras, review.Issues, review.Warnings);
                }).ToArray())).ToArray();
            var cameraViews = cameras.Values.OrderBy(c => c.Code).Select(c =>
            {
                var connection = connections.GetValueOrDefault(c.CameraId);
                var cameraEvents = events.Where(e => e.CameraId == c.CameraId).ToArray();
                var issues = new List<MonitoringIssue>();
                if (c.Status != "ACTIVE") issues.Add(new("CAMERA_NOT_ACTIVE", "Camera is inactive."));
                var valid = false;
                if (connection is null) issues.Add(new("CONNECTION_MISSING", "Configure a video source."));
                else
                {
                    try { Rules.Connection(connection); valid = true; }
                    catch (DomainException e) { issues.Add(new(e.Code, "Fix the video source configuration.")); }
                    if (connection.LastTestResult != "SUCCESS" || connection.LastTestedAt is null)
                        issues.Add(new("CONNECTION_NOT_TESTED", "Test the current connection and preview a frame."));
                    if (!connection.IsEnabled) issues.Add(new("CONNECTION_DISABLED", "Enable the tested connection."));
                }
                var health = runtime.Snapshot(c, cameraEvents.Select(e => e.EventType), connection?.IsEnabled == true && valid);
                if (connection is not null && valid)
                {
                    if (connection.IsEnabled && c.Status == "ACTIVE" && c.HealthStatus != "ONLINE")
                        issues.Add(new("CAMERA_HEALTH_" + c.HealthStatus, c.HealthStatus == "UNKNOWN" ? "No successful health observation yet." : "Investigate camera connectivity."));
                    if (connection.IsEnabled && c.Status == "ACTIVE" && health.ProcessingAvailability == "UNAVAILABLE")
                        issues.Add(new("PROCESSING_UNAVAILABLE", "Camera frames are arriving, but visual-health processing is unavailable."));
                    foreach (var eventType in health.ActiveHealthIssues.Where(CameraHealthEventTypes.IsVisual))
                        issues.Add(new(eventType, eventType switch
                        {
                            CameraHealthEventTypes.ViewBlocked => "The camera view appears blocked.",
                            CameraHealthEventTypes.ViewBlurred => "The camera view appears too blurred for reliable monitoring.",
                            CameraHealthEventTypes.ViewFrozen => "The camera stream appears frozen.",
                            _ => "The camera frame is invalid for monitoring."
                        }));
                }
                var healthStatus = valid ? c.HealthStatus : "UNKNOWN";
                return new SetupCamera(c.CameraId, c.FloorId, floors.Single(f => f.FloorId == c.FloorId).Name, c.Code, c.Name,
                    c.Status, healthStatus, health.MonitoringReadiness, health.ProcessingAvailability, health.ActiveHealthIssues,
                    c.LastSeenAt, connection is not null, valid, connection?.IsEnabled ?? false,
                    connection?.SourceType, connection?.Protocol, connection?.LastTestResult, connection?.LastTestedAt, issues.ToArray());
            }).ToArray();
            var zoneViews = floorViews.SelectMany(f => f.Zones).ToArray();
            var enabled = cameraViews.Where(c => c.Status == "ACTIVE" && c.IsEnabled && c.ConnectionValid).ToArray();
            var totals = new SetupTotals(floors.Count, zones.Count, cameras.Count, configurations.Count,
                zoneViews.Count(z => z.Configuration?.Status == "ACTIVE"), zoneViews.Count(z => z.CanActivate),
                enabled.Count(c => c.HealthStatus == "ONLINE"), enabled.Length, events.Count);
            SetupStep[] steps = [
                new("floor-zones", "Floor plans & zones", floorViews.Count(f => f.HasMap && f.Zones.Length > 0), floors.Count, "Floors with a saved map and at least one zone."),
                new("camera-source", "Camera sources", cameraViews.Count(c => c.HasConnection && c.ConnectionValid), cameras.Count, "Registered cameras with a valid saved source."),
                new("test-enable", "Test & enable", cameraViews.Count(c => c.Status == "ACTIVE" && c.ConnectionValid && c.IsEnabled && c.LastTestResult == "SUCCESS" && c.LastTestedAt is not null), cameras.Count, "Tested, enabled sources. Open Cameras to preview; preview completion is not recorded."),
                new("mapping-roi", "Camera mapping & ROI", zoneViews.Count(z => z.Cameras.Any(c => c.RoiPolygon.Length >= 3)), zones.Count, "Zones with a same-floor camera mapping and valid camera-frame ROI."),
                new("rules", "Incident rules", zoneViews.Count(z => z.Configuration?.RuleCount > 0), zones.Count, "Zones with saved incident rules. Readiness also checks enabled supported rules and required area."),
                new("activation", "Configuration activation", totals.ActiveConfigurationCount, zones.Count, "Active configurations per zone; remaining zones can be configured independently.")
            ];
            var eventViews = events.OrderByDescending(e => e.DetectedAt).Select(e => new SetupHealthEvent(e.HealthEventId, e.CameraId,
                cameras[e.CameraId].Code, e.EventType, e.Status, e.DetectedAt)).ToArray();
            return new SetupOverviewView(clock.UtcNow, supermarket is not null, totals, steps, floorViews, cameraViews, eventViews);
        }, ct);
    }
}
