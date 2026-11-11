using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Supermarket.Domain;

namespace Supermarket.Application;

// Shared saved-data readiness only. No camera probing, credential resolution or runtime-health claims.
internal static class CheckoutSnapshotPreparation
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal sealed record Prepared(CheckoutMappingSnapshot[] Mappings, MonitoringCameraView[] Cameras,
        IReadOnlyDictionary<Guid, CameraConnection> Connections, MonitoringIssue[] Issues, string Version);

    internal static void Limit(int count, int configured, int ceiling, string resource)
    {
        if (configured < 1 || configured > ceiling || count > configured)
            throw new ApplicationException("CHECKOUT_LIMIT_EXCEEDED", $"{resource} exceeds the supported checkout limit ({Math.Clamp(configured, 1, ceiling)}).");
    }

    internal static async Task<Prepared> Read(ISetupStore store, CheckoutMonitoringOptions options, Zone zone,
        MonitoringConfiguration configuration, MonitoringRule[] rules, MonitoringRuleSnapshot[] snapshots,
        ICheckoutSchema? schema, ICheckoutConfigurationQueries? queries, CancellationToken ct)
    {
        if (schema is null || !await schema.Ready(ct))
            throw new ApplicationException("CHECKOUT_SCHEMA_NOT_READY", "Checkout configuration schema is not ready; monitoring remains disabled.", 503);
        var issues = new List<MonitoringIssue>();
        if (!options.AiContractEnabled)
            issues.Add(new("CHECKOUT_AI_CONTRACT_DISABLED", "Checkout monitoring requires an explicitly enabled, validated checkout AI contract; the generic AI protocol does not provide these observations."));
        if (zone.Status != "ACTIVE") issues.Add(new("ZONE_INACTIVE", "Activate the zone before monitoring."));
        if (zone.ZoneType != "CHECKOUT") issues.Add(new("CHECKOUT_ZONE_REQUIRED", "Checkout capacity monitoring requires a CHECKOUT zone."));
        try { MonitoringPolicy.Confidence(configuration.ConfidenceThreshold); }
        catch (DomainException e) { issues.Add(new(e.Code, e.Message)); }

        // Growth: zones -> counters -> sources. Reject incomplete coverage rather than truncate it.
        Limit(0, options.MaxCountersPerZone, 128, "Enabled counters per zone");
        Limit(0, options.MaxSourcesPerCounter, 16, "Enabled sources per counter");
        Limit(0, options.MaxMappingsPerCamera, 128, "Enabled mappings per camera");
        var counterRows = queries is null ? await store.List<CheckoutCounter>(c => c.ZoneId == zone.ZoneId, ct)
            : await queries.CountersByZone(zone.ZoneId, options.MaxCountersPerZone, ct);
        Limit(counterRows.Count, options.MaxCountersPerZone, 128, "Counters per zone");
        var counters = counterRows.Where(c => c.IsEnabled).ToList();
        Limit(counters.Count, options.MaxCountersPerZone, 128, "Enabled counters per zone");
        if (counters.Count == 0) issues.Add(new("CHECKOUT_COUNTERS_REQUIRED", "Enable at least one checkout counter in this zone."));
        var mappings = new List<CameraCheckoutMapping>();
        foreach (var counter in counters.OrderBy(c => c.CheckoutCounterId))
        {
            var sourceRows = queries is null ? await store.List<CameraCheckoutMapping>(m => m.CheckoutCounterId == counter.CheckoutCounterId, ct)
                : await queries.MappingsByCounter(counter.CheckoutCounterId, options.MaxSourcesPerCounter, ct);
            Limit(sourceRows.Count, options.MaxSourcesPerCounter, 16, "Sources per counter");
            var sources = sourceRows.Where(m => m.IsEnabled).ToList();
            Limit(sources.Count, options.MaxSourcesPerCounter, 16, "Enabled sources per counter");
            if (sources.Count == 0) issues.Add(new("CHECKOUT_SOURCE_REQUIRED", $"Counter {counter.Code} needs an enabled lane/staff camera mapping."));
            if (sources.Count(m => m.IsQueueSource) != 1)
                issues.Add(new("CHECKOUT_QUEUE_SOURCE_REQUIRED", $"Counter {counter.Code} requires exactly one enabled queue source; sources are never automatically substituted."));
            mappings.AddRange(sources);
        }

        var cameras = new Dictionary<Guid, Camera>();
        var connections = new Dictionary<Guid, CameraConnection>();
        foreach (var cameraId in mappings.Select(m => m.CameraId).Distinct().Order())
        {
            var cameraMappings = queries is null ? await store.List<CameraCheckoutMapping>(m => m.CameraId == cameraId, ct)
                : await queries.MappingsByCamera(cameraId, options.MaxMappingsPerCamera, ct);
            Limit(cameraMappings.Count, options.MaxMappingsPerCamera, 128, "Enabled mappings per camera");
            var camera = await store.Find<Camera>(cameraId, ct);
            if (camera is not null) cameras[cameraId] = camera;
            var sources = await store.List<CameraConnection>(c => c.CameraId == cameraId, ct);
            if (sources.Count > 1) issues.Add(new("CONNECTION_AMBIGUOUS", "Keep exactly one video source per checkout camera."));
            else if (sources.SingleOrDefault() is { } connection) connections[cameraId] = connection;
        }

        var views = new List<MonitoringCameraView>();
        var prepared = new List<CheckoutMappingSnapshot>();
        foreach (var mapping in mappings.OrderBy(m => m.MappingId))
        {
            var problems = new List<MonitoringIssue>();
            var camera = cameras.GetValueOrDefault(mapping.CameraId);
            var connection = connections.GetValueOrDefault(mapping.CameraId);
            var lane = Polygon(mapping.LaneRoi, "Lane", problems);
            var staff = Polygon(mapping.StaffRoi, "Staff", problems);
            if (camera is null) problems.Add(new("CAMERA_MISSING", "A checkout camera no longer exists."));
            else
            {
                try { Rules.SameFloor(camera, zone); }
                catch (DomainException e) { problems.Add(new(e.Code, e.Message)); }
                if (camera.Status != "ACTIVE") problems.Add(new("CAMERA_NOT_ACTIVE", "Activate the checkout camera."));
            }
            if (connection is null) problems.Add(new("CONNECTION_MISSING", "Configure a video source for the checkout camera."));
            else
            {
                try { Rules.Connection(connection); }
                catch (DomainException e) { problems.Add(new(e.Code, "Fix the checkout video source configuration.")); }
                if (!connection.IsEnabled || connection.LastTestedAt is null || connection.LastTestResult != "SUCCESS")
                    problems.Add(new("CONNECTION_NOT_READY", "Test and enable the current checkout video source."));
                if (!(connection.SourceType == "LIVE" && connection.Protocol is "HTTP" or "RTSP" or "HLS")
                    && !(connection.SourceType == "RECORDED" && connection.Protocol == "FILE"))
                    problems.Add(new("AI_SOURCE_UNSUPPORTED", "Checkout AI requires live HTTP/RTSP/HLS or uploaded recorded video."));
            }
            issues.AddRange(problems);
            // Lane ROI is a display preview only; it never becomes a generic zone measurement source.
            views.Add(new(mapping.CameraId, camera?.Code ?? "", camera?.Name ?? "Missing checkout camera", camera?.Status ?? "MISSING",
                "ACTIVE", lane, connection?.SourceType, connection?.Protocol, connection?.IsEnabled ?? false,
                connection?.LastTestResult, connection?.LastTestedAt, problems.Count == 0, problems.ToArray()));
            prepared.Add(new(mapping.MappingId, mapping.CameraId, mapping.CheckoutCounterId, zone.ZoneId, zone.Name,
                configuration.ConfigId, configuration.UpdatedAt, mapping.UpdatedAt, lane, staff, mapping.IsQueueSource,
                configuration.ConfidenceThreshold, snapshots, problems.FirstOrDefault()?.Code));
        }
        var blockers = issues.Distinct().ToArray();
        var issueCode = blockers.FirstOrDefault()?.Code;
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            zone.ZoneId, zone.Name, zone.FloorId, zone.ZoneType, zone.Status,
            configuration, rules = rules.OrderBy(r => r.RuleId), options,
            counters = counters.OrderBy(c => c.CheckoutCounterId), mappings = mappings.OrderBy(m => m.MappingId),
            // Health probes also update Camera.UpdatedAt; only actual camera configuration identifies this source.
            cameras = cameras.Values.OrderBy(c => c.CameraId).Select(c => new { c.CameraId, c.Code, c.Name, c.FloorId, c.Status }),
            connections = connections.Values.OrderBy(c => c.CameraId).Select(c => new
            {
                c.CameraId, c.ConnectionId, c.SourceType, c.Protocol, c.StreamUri, c.Username, c.CredentialSecretRef,
                c.IsEnabled, c.UpdatedAt, c.LastTestResult, c.LastTestedAt
            }), blockers
        }, Json))));
        return new(prepared.Select(m => m with { IssueCode = issueCode ?? m.IssueCode }).ToArray(),
            views.ToArray(), connections, blockers, version);
    }

    internal static async Task<List<CameraCheckoutMapping>> CameraMappings(ISetupStore store, ICheckoutConfigurationQueries? queries,
        CheckoutMonitoringOptions options, Guid cameraId, CancellationToken ct)
    {
        Limit(0, options.MaxMappingsPerCamera, 128, "Mappings per camera");
        var mappings = queries is null ? await store.List<CameraCheckoutMapping>(m => m.CameraId == cameraId, ct)
            : await queries.MappingsByCamera(cameraId, options.MaxMappingsPerCamera, ct);
        Limit(mappings.Count, options.MaxMappingsPerCamera, 128, "Mappings per camera");
        return mappings.Where(m => m.IsEnabled).ToList();
    }

    private static Point[] Polygon(string json, string name, List<MonitoringIssue> issues)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 64000)
                throw new DomainException("ROI_INVALID", $"Save a valid {name.ToLowerInvariant()} ROI with at most 1000 vertices.");
            var points = JsonSerializer.Deserialize<Point[]>(json, Json) ?? [];
            Rules.Polygon(points);
            return points;
        }
        catch (JsonException) { issues.Add(new("ROI_INVALID", $"Save a valid checkout {name.ToLowerInvariant()} ROI.")); }
        catch (DomainException e) { issues.Add(new(e.Code, $"{name} ROI: {e.Message}")); }
        return [];
    }
}
