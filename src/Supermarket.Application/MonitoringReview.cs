using System.Text.Json;
using Supermarket.Domain;
namespace Supermarket.Application;

public sealed partial class MonitoringSetup
{
    private async Task<MonitoringReviewView> BuildReview(Guid zoneId, CancellationToken ct)
    {
        var zone = UseCase.Found(await store.Find<Zone>(zoneId, ct));
        var configuration = await Configuration(zoneId, ct);
        var rules = (await store.List<MonitoringRule>(r => r.ConfigId == configuration.ConfigId, ct)).ToArray();
        var types = (await store.List<IncidentType>(ct: ct)).ToDictionary(t => t.IncidentTypeId);
        var mappings = await store.List<CameraZoneMapping>(m => m.ZoneId == zoneId && m.Status == "ACTIVE", ct);
        var cameras = new Dictionary<Guid, Camera>();
        var connections = new Dictionary<Guid, CameraConnection>();
        foreach (var mapping in mappings)
        {
            cameras[mapping.CameraId] = UseCase.Found(await store.Find<Camera>(mapping.CameraId, ct));
            var connection = (await store.List<CameraConnection>(c => c.CameraId == mapping.CameraId, ct)).SingleOrDefault();
            if (connection is not null) connections[mapping.CameraId] = connection;
        }
        return EvaluateReview(zone, ConfigurationView(configuration, rules, types), rules, types, mappings, cameras, connections);
    }

    // Dashboard and activation share saved-data readiness. Operational health is reported separately.
    internal static MonitoringReviewView EvaluateReview(Zone zone, MonitoringConfigurationView configuration,
        MonitoringRule[] rules, IReadOnlyDictionary<Guid, IncidentType> types, IEnumerable<CameraZoneMapping> mappings,
        IReadOnlyDictionary<Guid, Camera> cameraRecords, IReadOnlyDictionary<Guid, CameraConnection> connections)
    {
        var issues = new List<MonitoringIssue>();
        if (zone.Status != "ACTIVE") issues.Add(new("ZONE_INACTIVE", "Activate the zone before monitoring."));
        try { MonitoringPolicy.Confidence(configuration.ConfidenceThreshold); }
        catch (DomainException e) { issues.Add(new(e.Code, e.Message)); }
        var enabled = rules.Where(r => r.Enabled).ToArray();
        if (enabled.Length == 0) issues.Add(new("NO_ENABLED_RULES", "Enable at least one supported AI rule."));
        foreach (var rule in enabled)
        {
            var type = types.GetValueOrDefault(rule.IncidentTypeId);
            if (type is null) { issues.Add(new("INCIDENT_TYPE_MISSING", "A rule's incident type no longer exists.")); continue; }
            try { MonitoringPolicy.ValidateForActivation(rule, type); }
            catch (DomainException e) { issues.Add(new(e.Code, $"{type.Name}: {e.Message}")); }
            if (MonitoringPolicy.RuntimeDefinition(rule,type).Mode == "CROWD_DENSITY" && zone.AreaM2 is not > 0)
                issues.Add(new("ZONE_AREA_REQUIRED", "Enter the zone's physical area in m² before enabling density monitoring."));
        }
        var cameras = MappedCameras(zone, mappings, cameraRecords, connections);
        if (cameras.Length > 1) issues.Add(new("MEASUREMENT_SOURCE_AMBIGUOUS", "Keep exactly one active camera mapping for this zone; overlapping cameras are not combined."));
        if (!cameras.Any(c => c.Ready)) issues.Add(new("CAMERA_NOT_READY", "Map an active camera with valid ROI and tested, enabled AI-compatible source."));
        var warnings = new List<string>();
        if (cameras.Any(c => c.SourceType == "RECORDED")) warnings.Add("Recorded video is a controlled test source, not live operational CCTV.");
        warnings.Add("Activation requests backend-owned monitoring. Check runtime separately; incident dispatch/tasks are not implemented in this increment.");
        return new(configuration, StoreSetup.View(zone), cameras, issues.ToArray(), warnings.ToArray(), issues.Count == 0);
    }

    internal static MonitoringCameraView[] MappedCameras(Zone zone, IEnumerable<CameraZoneMapping> mappings,
        IReadOnlyDictionary<Guid, Camera> cameraRecords, IReadOnlyDictionary<Guid, CameraConnection> connections)
    {
        var cameras = new List<MonitoringCameraView>();
        foreach (var mapping in mappings.Where(m => m.ZoneId == zone.ZoneId && m.Status == "ACTIVE"))
        {
            var camera = UseCase.Found(cameraRecords.GetValueOrDefault(mapping.CameraId));
            var connection = connections.GetValueOrDefault(camera.CameraId);
            var problems = new List<MonitoringIssue>();
            Point[] roi = [];
            try
            {
                Rules.SameFloor(camera, zone);
                var points = JsonSerializer.Deserialize<Point[]>(mapping.RoiPolygon) ?? [];
                Rules.Polygon(points);
                roi = points;
            }
            catch (JsonException) { problems.Add(new("ROI_INVALID", "Save a valid camera-frame ROI.")); }
            catch (DomainException e) { problems.Add(new(e.Code, e.Message)); }
            if (camera.Status != "ACTIVE") problems.Add(new("CAMERA_NOT_ACTIVE", "Activate the camera."));
            if (connection is null) problems.Add(new("CONNECTION_MISSING", "Configure a video source."));
            else
            {
                try { Rules.Connection(connection); }
                catch (DomainException e) { problems.Add(new(e.Code, "Fix the video source configuration.")); }
                if (!connection.IsEnabled || connection.LastTestResult != "SUCCESS" || connection.LastTestedAt is null)
                    problems.Add(new("CONNECTION_NOT_READY", "Test and enable the current video source."));
                if (!(connection.SourceType == "LIVE" && connection.Protocol is "HTTP" or "RTSP" or "HLS")
                    && !(connection.SourceType == "RECORDED" && connection.Protocol == "FILE"))
                    problems.Add(new("AI_SOURCE_UNSUPPORTED", "AI requires live HTTP/RTSP/HLS or uploaded recorded video."));
            }
            cameras.Add(new(camera.CameraId, camera.Code, camera.Name, camera.Status, mapping.Status, roi, connection?.SourceType, connection?.Protocol,
                connection?.IsEnabled ?? false, connection?.LastTestResult, connection?.LastTestedAt, problems.Count == 0, problems.ToArray()));
        }
        return cameras.ToArray();
    }
}
