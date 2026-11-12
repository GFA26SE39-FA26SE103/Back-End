using System.Text.Json;
using Supermarket.Domain;

namespace Supermarket.Application;

public sealed class CheckoutConfiguration
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ISetupStore store;
    private readonly ICurrentUser current;
    private readonly ICheckoutConfigurationQueries queries;
    private readonly ICheckoutSchema schema;
    private readonly int maxCounters;
    private readonly int maxCounterMappings;
    private readonly int maxCameraMappings;

    public CheckoutConfiguration(ISetupStore store, ICurrentUser current, ICheckoutConfigurationQueries queries,
        ICheckoutSchema schema, CheckoutConfigurationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsValid())
            throw new ArgumentOutOfRangeException(nameof(options), "Checkout capacities must be positive and leave room for an overflow row.");
        this.store = store; this.current = current; this.queries = queries; this.schema = schema;
        maxCounters = options.MaxCountersPerZone;
        maxCounterMappings = options.MaxMappingsPerCounter;
        maxCameraMappings = options.MaxMappingsPerCamera;
    }

    public async Task<List<CheckoutCounterView>> CountersByZone(Guid zoneId, CancellationToken ct)
    {
        UseCase.Viewer(current); await RequireSchema(ct);
        UseCase.Found(await store.Find<Zone>(zoneId, ct));
        return (await CounterRows(zoneId, ct)).Select(View).ToList();
    }

    public async Task<CheckoutCounterView> Get(Guid counterId, CancellationToken ct)
    {
        UseCase.Viewer(current); await RequireSchema(ct);
        return View(UseCase.Found(await store.Find<CheckoutCounter>(counterId, ct)));
    }

    public async Task<List<CheckoutMappingView>> MappingsByCamera(Guid cameraId, CancellationToken ct)
    {
        UseCase.Viewer(current); await RequireSchema(ct);
        UseCase.Found(await store.Find<Camera>(cameraId, ct));
        return (await CameraMappings(cameraId, ct)).Select(View).ToList();
    }

    public async Task<List<CheckoutMappingView>> MappingsByCounter(Guid counterId, CancellationToken ct)
    {
        UseCase.Viewer(current); await RequireSchema(ct);
        UseCase.Found(await store.Find<CheckoutCounter>(counterId, ct));
        return (await CounterMappings(counterId, ct)).Select(View).ToList();
    }

    public async Task<CheckoutCounterView> Create(Guid zoneId, CheckoutCounterRequest request, CancellationToken ct)
    {
        UseCase.Admin(current); await RequireSchema(ct);
        return await store.Transaction(async () =>
        {
            var zone = UseCase.Found(await store.Find<Zone>(zoneId, ct));
            await MutableZone(zone, ct);
            CheckVersion(null, request.ExpectedUpdatedAt);
            var code = Rules.Text(request.Code, 50, "Code");
            var name = Rules.Text(request.Name, 100, "Name");
            var counters = await CounterRows(zoneId, ct);
            UseCase.Unique(counters.Any(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)));
            RequireSpace(counters.Count, maxCounters);
            if (request.IsEnabled)
            {
                ActiveZone(zone);
                QueueSources([], true); // A new counter has no mappings yet; create it disabled first.
            }
            var counter = new CheckoutCounter { CheckoutCounterId = Guid.NewGuid(), ZoneId = zoneId,
                Code = code, Name = name, IsEnabled = request.IsEnabled };
            await store.Add(counter, ct);
            return View(counter);
        }, ct);
    }

    public async Task<CheckoutCounterView> Update(Guid counterId, CheckoutCounterRequest request, CancellationToken ct)
    {
        UseCase.Admin(current); await RequireSchema(ct);
        return await store.Transaction(async () =>
        {
            var counter = UseCase.Found(await store.Find<CheckoutCounter>(counterId, ct));
            var zone = UseCase.Found(await store.Find<Zone>(counter.ZoneId, ct));
            await MutableZone(zone, ct);
            CheckVersion(counter.UpdatedAt, request.ExpectedUpdatedAt);
            var code = Rules.Text(request.Code, 50, "Code");
            var name = Rules.Text(request.Name, 100, "Name");
            var counters = await CounterRows(counter.ZoneId, ct);
            UseCase.Unique(counters.Any(c => c.CheckoutCounterId != counterId
                && string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)));
            if (request.IsEnabled)
            {
                ActiveZone(zone);
                var mappings = await CounterMappings(counterId, ct);
                QueueSources(mappings, true);
                if (!counter.IsEnabled)
                    foreach (var mapping in mappings.Where(m => m.IsEnabled))
                        await ReadyMapping(mapping, zone, ct);
            }
            // Every rejection above must leave even a reference-returning memory store unchanged.
            counter.Code = code; counter.Name = name; counter.IsEnabled = request.IsEnabled;
            await store.Update(counter, ct);
            return View(counter);
        }, ct);
    }

    public async Task Delete(Guid counterId, DateTime? expectedUpdatedAt, CancellationToken ct)
    {
        UseCase.Admin(current); await RequireSchema(ct);
        await store.Transaction(async () =>
        {
            var counter = UseCase.Found(await store.Find<CheckoutCounter>(counterId, ct));
            var zone = UseCase.Found(await store.Find<Zone>(counter.ZoneId, ct));
            await MutableZone(zone, ct);
            CheckVersion(counter.UpdatedAt, expectedUpdatedAt);
            if (counter.IsEnabled)
                throw new ApplicationException("CHECKOUT_COUNTER_ENABLED", "Disable the checkout counter before deleting it.");
            var mappings = await CounterMappings(counterId, ct);
            if (mappings.Any(m => m.IsEnabled))
                throw new ApplicationException("CHECKOUT_MAPPING_ENABLED", "Disable all camera mappings before deleting the checkout counter.");
            foreach (var mapping in mappings) await store.Remove(mapping, ct);
            await store.Remove(counter, ct);
            return true;
        }, ct);
    }

    public async Task<CheckoutMappingView> PutMapping(Guid counterId, Guid cameraId, CheckoutMappingRequest request, CancellationToken ct)
    {
        UseCase.Admin(current); await RequireSchema(ct);
        return await store.Transaction(async () =>
        {
            var counter = UseCase.Found(await store.Find<CheckoutCounter>(counterId, ct));
            var zone = UseCase.Found(await store.Find<Zone>(counter.ZoneId, ct));
            await MutableZone(zone, ct);
            var camera = UseCase.Found(await store.Find<Camera>(cameraId, ct));
            Rules.SameFloor(camera, zone);
            var mappings = await CounterMappings(counterId, ct);
            var cameraMappings = await CameraMappings(cameraId, ct);
            UniquePairs(mappings); UniquePairs(cameraMappings);
            var existing = mappings.SingleOrDefault(m => m.CameraId == cameraId);
            CheckVersion(existing?.UpdatedAt, request.ExpectedUpdatedAt);
            Rules.Polygon(request.LaneRoi); Rules.Polygon(request.StaffRoi);
            if (request.ServiceRoi is not null) Rules.Polygon(request.ServiceRoi);
            if (request.IsEnabled && request.IsQueueSource)
                Rules.Require(request.ServiceRoi is not null, "CHECKOUT_SERVICE_ROI_REQUIRED", "Draw Service ROI before enabling the queue source.");
            if (existing is null)
            {
                RequireSpace(mappings.Count, maxCounterMappings);
                RequireSpace(cameraMappings.Count, maxCameraMappings);
            }
            var proposed = new CameraCheckoutMapping { MappingId = existing?.MappingId ?? Guid.NewGuid(),
                CameraId = cameraId, CheckoutCounterId = counterId,
                LaneRoi = JsonSerializer.Serialize(request.LaneRoi, Json), StaffRoi = JsonSerializer.Serialize(request.StaffRoi, Json),
                ServiceRoi = request.ServiceRoi is null ? null : JsonSerializer.Serialize(request.ServiceRoi, Json),
                IsEnabled = request.IsEnabled, IsQueueSource = request.IsQueueSource,
                CreatedAt = existing?.CreatedAt ?? default, UpdatedAt = existing?.UpdatedAt ?? default };
            var after = mappings.Where(m => m.CameraId != cameraId).Append(proposed).ToList();
            QueueSources(after, counter.IsEnabled);
            if (request.IsEnabled)
            {
                ActiveZone(zone);
                await ReadyCamera(camera, ct);
            }
            // Detached proposed state prevents invalid PUTs from mutating the existing tracked object.
            if (existing is null) await store.Add(proposed, ct); else await store.Update(proposed, ct);
            return View(proposed);
        }, ct);
    }

    public async Task DeleteMapping(Guid counterId, Guid cameraId, DateTime? expectedUpdatedAt, CancellationToken ct)
    {
        UseCase.Admin(current); await RequireSchema(ct);
        await store.Transaction(async () =>
        {
            var counter = UseCase.Found(await store.Find<CheckoutCounter>(counterId, ct));
            var zone = UseCase.Found(await store.Find<Zone>(counter.ZoneId, ct));
            await MutableZone(zone, ct);
            var mappings = await CounterMappings(counterId, ct);
            UniquePairs(mappings);
            var existing = UseCase.Found(mappings.SingleOrDefault(m => m.CameraId == cameraId));
            CheckVersion(existing.UpdatedAt, expectedUpdatedAt);
            QueueSources(mappings.Where(m => m.MappingId != existing.MappingId).ToList(), counter.IsEnabled);
            await store.Remove(existing, ct);
            return true;
        }, ct);
    }

    private async Task RequireSchema(CancellationToken ct)
    {
        if (!await schema.Ready(ct))
            throw new ApplicationException("CHECKOUT_SCHEMA_NOT_READY", "The approved checkout schema is not ready in this environment.", 503);
    }

    private async Task MutableZone(Zone zone, CancellationToken ct)
    {
        if ((await store.List<MonitoringConfiguration>(m => m.ZoneId == zone.ZoneId && m.Status == "ACTIVE", ct)).Count != 0)
            throw new ApplicationException("MONITORING_ACTIVE", "Deactivate zone monitoring before changing checkout configuration.");
        Rules.Require(zone.ZoneType == "CHECKOUT", "CHECKOUT_ZONE_REQUIRED", "Checkout counters require a CHECKOUT zone.");
    }

    private static void ActiveZone(Zone zone)
        => Rules.Require(zone.Status == "ACTIVE", "CHECKOUT_ZONE_INACTIVE", "Enable checkout configuration only in an active zone.");

    private async Task ReadyMapping(CameraCheckoutMapping mapping, Zone zone, CancellationToken ct)
    {
        Rules.Polygon(Points(mapping.LaneRoi)); Rules.Polygon(Points(mapping.StaffRoi));
        if (mapping.IsQueueSource) Rules.Polygon(Points(mapping.ServiceRoi ?? "[]"));
        var camera = UseCase.Found(await store.Find<Camera>(mapping.CameraId, ct));
        Rules.SameFloor(camera, zone);
        await ReadyCamera(camera, ct);
    }

    private async Task ReadyCamera(Camera camera, CancellationToken ct)
    {
        // Configuration readiness only: runtime health/online state never changes administrative intent.
        var connections = await store.List<CameraConnection>(c => c.CameraId == camera.CameraId, ct);
        if (camera.Status != "ACTIVE" || connections.Count != 1 || !connections[0].IsEnabled
            || connections[0].LastTestedAt is null || connections[0].LastTestResult != "SUCCESS")
            throw new ApplicationException("CHECKOUT_CAMERA_NOT_READY", "An active camera with a tested, enabled connection is required.");
    }

    private static void QueueSources(List<CameraCheckoutMapping> mappings, bool counterEnabled)
    {
        UniquePairs(mappings);
        if (mappings.Any(m => m.IsQueueSource && !m.IsEnabled))
            throw new ApplicationException("CHECKOUT_QUEUE_SOURCE_DISABLED", "A queue source must be an enabled mapping.");
        if (mappings.Any(m => m.IsEnabled && m.IsQueueSource && m.ServiceRoi is null))
            throw new ApplicationException("CHECKOUT_SERVICE_ROI_REQUIRED", "Draw Service ROI before enabling a queue source.");
        var sources = mappings.Count(m => m.IsEnabled && m.IsQueueSource);
        if (sources > 1)
            throw new ApplicationException("CHECKOUT_QUEUE_SOURCE_DUPLICATE", "Only one enabled queue source is allowed per checkout counter.");
        if (counterEnabled && sources != 1)
            throw new ApplicationException("CHECKOUT_QUEUE_SOURCE_REQUIRED", "An enabled checkout counter requires an enabled mapping and exactly one enabled queue source.");
    }

    private static void UniquePairs(List<CameraCheckoutMapping> mappings)
    {
        if (mappings.Select(m => (m.CameraId, m.CheckoutCounterId)).Distinct().Count() != mappings.Count)
            throw new ApplicationException("CHECKOUT_MAPPING_DUPLICATE", "Only one mapping per camera and checkout counter is allowed.");
    }

    private static void CheckVersion(DateTime? actual, DateTime? expected)
    {
        // SQL datetime2 timestamps are UTC; unspecified values must not depend on the host timezone.
        static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
        if (actual is null ? expected is not null : expected is null || Utc(actual.Value) != Utc(expected.Value))
            throw new ApplicationException("CHECKOUT_CONFIGURATION_CHANGED", "Checkout configuration changed; reload and retry with its current updatedAt.");
    }

    private async Task<List<CheckoutCounter>> CounterRows(Guid zoneId, CancellationToken ct)
        => WithinCapacity(await queries.CountersByZone(zoneId, maxCounters, ct), maxCounters);
    private async Task<List<CameraCheckoutMapping>> CounterMappings(Guid counterId, CancellationToken ct)
        => WithinCapacity(await queries.MappingsByCounter(counterId, maxCounterMappings, ct), maxCounterMappings);
    private async Task<List<CameraCheckoutMapping>> CameraMappings(Guid cameraId, CancellationToken ct)
        => WithinCapacity(await queries.MappingsByCamera(cameraId, maxCameraMappings, ct), maxCameraMappings);

    private static List<T> WithinCapacity<T>(List<T> rows, int max)
    {
        if (rows.Count > max) throw Capacity();
        return rows;
    }
    private static void RequireSpace(int count, int max) { if (count >= max) throw Capacity(); }
    private static ApplicationException Capacity()
        => new("CHECKOUT_CAPACITY_EXCEEDED", "Checkout configuration exceeds the configured scoped capacity.");

    private static Point[] Points(string json)
    {
        try { return JsonSerializer.Deserialize<Point[]>(json, Json) ?? []; }
        catch (JsonException) { throw new DomainException("INVALID_POLYGON", "Checkout ROI must be a valid point array."); }
    }
    private static CheckoutCounterView View(CheckoutCounter c)
        => new(c.CheckoutCounterId, c.ZoneId, c.Code, c.Name, c.IsEnabled, c.CreatedAt, c.UpdatedAt);
    private static CheckoutMappingView View(CameraCheckoutMapping m)
        => new(m.MappingId, m.CameraId, m.CheckoutCounterId, Points(m.LaneRoi), Points(m.StaffRoi),
            m.IsEnabled, m.IsQueueSource, m.CreatedAt, m.UpdatedAt,
            m.ServiceRoi is null ? null : Points(m.ServiceRoi));
}
