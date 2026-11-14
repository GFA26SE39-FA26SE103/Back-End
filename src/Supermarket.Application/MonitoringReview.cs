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
        if (rules.Length > 32) throw new ApplicationException("TOO_MANY_RULES", "A configuration supports at most 32 rules.");
        var types = (await store.List<IncidentType>(ct: ct)).ToDictionary(t => t.IncidentTypeId);
        var eventTypes = (await store.List<OperationalEventType>(ct: ct)).ToDictionary(t => t.EventTypeId);
        var checkoutRules = rules.Where(r => r.Enabled && r.OutcomeType == "OPERATIONAL_EVENT"
            && r.EventTypeId is { } id && (eventTypes.GetValueOrDefault(id)?.Code == "CHECKOUT_CAPACITY"
                || zone.ZoneType == "CHECKOUT" && eventTypes.GetValueOrDefault(id)?.Code == "EXCESSIVE_WAIT")).ToArray();
        var genericRules = rules.Except(checkoutRules).ToArray();
        var hasGenericRules = genericRules.Any(r => r.Enabled);
        var mappings = checkoutRules.Length == 0 || hasGenericRules
            ? await store.List<CameraZoneMapping>(m => m.ZoneId == zoneId && m.Status == "ACTIVE", ct) : [];
        var cameras = new Dictionary<Guid, Camera>();
        var connections = new Dictionary<Guid, CameraConnection>();
        foreach (var cameraId in mappings.Select(m => m.CameraId).Distinct())
        {
            cameras[cameraId] = UseCase.Found(await store.Find<Camera>(cameraId, ct));
            var connection = (await store.List<CameraConnection>(c => c.CameraId == cameraId, ct)).SingleOrDefault();
            if (connection is not null) connections[cameraId] = connection;
        }
        var view = ConfigurationView(configuration, rules, types, eventTypes);
        if (checkoutRules.Length == 0)
            return EvaluateReview(zone, view, rules, eventTypes, mappings, cameras, connections);

        var issues = new List<MonitoringIssue>();
        var snapshots = new List<MonitoringRuleSnapshot>();
        foreach (var rule in checkoutRules.OrderBy(r => r.RuleId))
        {
            var type = eventTypes[rule.EventTypeId!.Value];
            try { MonitoringPolicy.ValidateForActivation(rule, type); }
            catch (DomainException e) { issues.Add(new(e.Code, $"{type.Name}: {e.Message}")); }
            var definition = MonitoringPolicy.RuntimeDefinition(rule, type);
            snapshots.Add(new(rule.RuleId, null, type.Code, type.Name, definition.Mode, definition.Unit,
                rule.WarningThreshold, rule.CriticalThreshold, rule.SustainSec, rule.CooldownSec,
                type.EventTypeId, type.Code, rule.ClearDurationSec, rule.OutcomeType, rule.ParametersJson));
        }
        CheckoutSnapshotPreparation.Prepared checkoutReview;
        try
        {
            checkoutReview = await CheckoutSnapshotPreparation.Read(store, checkout, zone, configuration, checkoutRules, snapshots.ToArray(), checkoutSchema, checkoutQueries, ct);
        }
        catch (ApplicationException e) when (e.Code is "CHECKOUT_SCHEMA_NOT_READY" or "CHECKOUT_LIMIT_EXCEEDED")
        {
            checkoutReview = new([], [], new Dictionary<Guid, CameraConnection>(), [new(e.Code, e.Message)], "");
        }
        issues.AddRange(checkoutReview.Issues);
        var generic = hasGenericRules ? EvaluateReview(zone, view, genericRules, eventTypes, mappings, cameras, connections) : null;
        if (generic is not null) issues.AddRange(generic.Issues);
        var cameraViews = (generic?.Cameras ?? []).Concat(checkoutReview.Cameras).GroupBy(c => c.CameraId).Select(g =>
        {
            var first = g.First();
            return first with { Ready = g.All(c => c.Ready), Issues = g.SelectMany(c => c.Issues).Distinct().ToArray() };
        }).OrderBy(c => c.CameraId).ToArray();
        var blockers = issues.Distinct().ToArray();
        var warnings = (generic?.Warnings ?? ["Activation requests backend-owned monitoring; verify runtime separately."])
            .Concat(new[] { "Checkout camera lane ROIs are display previews only; checkout evaluation uses the independent lane/staff/service mapping." }).ToList();
        if (checkoutReview.Cameras.Any(c => c.SourceType == "RECORDED"))
            warnings.Add("Recorded video is a controlled test source, not live operational CCTV.");
        return new(view, StoreSetup.View(zone), cameraViews, blockers, warnings.Distinct().ToArray(), blockers.Length == 0);
    }

    // Dashboard and activation share saved-data readiness. Operational health is reported separately.
    internal static MonitoringReviewView EvaluateReview(Zone zone, MonitoringConfigurationView configuration,
        MonitoringRule[] rules, IReadOnlyDictionary<Guid, OperationalEventType> eventTypes, IEnumerable<CameraZoneMapping> mappings,
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
            if(rule.OutcomeType!="OPERATIONAL_EVENT") { issues.Add(new("INCIDENT_RULE_RUNTIME_UNSUPPORTED", "Direct Incident monitoring is not enabled until a validated measurement contract exists.")); continue; }
            var type = rule.EventTypeId is { } eventTypeId ? eventTypes.GetValueOrDefault(eventTypeId) : null;
            if (type is null) { issues.Add(new("OPERATIONAL_EVENT_TYPE_MISSING", "A rule's operational event type no longer exists.")); continue; }
            if (type.Code == "EXCESSIVE_WAIT" && zone.ZoneType != "CHECKOUT")
                issues.Add(new("CHECKOUT_ZONE_REQUIRED", "Waiting-time monitoring requires a checkout zone and Service ROI."));
            try { MonitoringPolicy.ValidateForActivation(rule, type); }
            catch (DomainException e) { issues.Add(new(e.Code, $"{type.Name}: {e.Message}")); }
            if (MonitoringPolicy.RuntimeDefinition(rule,type).Mode == "CROWD_DENSITY" && zone.AreaM2 is not > 0)
                issues.Add(new("ZONE_AREA_REQUIRED", "Enter the zone's physical area in m² before enabling density monitoring."));
        }
        var cameras = MappedCameras(zone, mappings, cameraRecords, connections);
        if (cameras.Select(c => c.CameraId).Distinct().Count() > 1)
            issues.Add(new("MEASUREMENT_SOURCE_AMBIGUOUS", "Keep exactly one active camera source for this zone; multiple ROIs on that camera are allowed."));
        if (cameras.Length == 0 || cameras.Any(c => !c.Ready))
            issues.Add(new("CAMERA_NOT_READY", "Every active ROI needs a valid polygon and a tested, enabled AI-compatible camera source."));
        var warnings = new List<string>();
        if (cameras.Any(c => c.SourceType == "RECORDED")) warnings.Add("Recorded video is a controlled test source, not live operational CCTV.");
        warnings.Add("Activation requests backend-owned monitoring. Check runtime separately; an ACTIVE configuration does not guarantee usable measurements.");
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
