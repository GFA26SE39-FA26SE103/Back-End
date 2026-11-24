using Supermarket.Domain;

namespace Supermarket.Application;

/// <summary>Protects shared camera sessions and zone identity without changing runtime health/configuration state.</summary>
public sealed class CheckoutSourceGuard(ISetupStore store, ICheckoutSchema schema,
    ICheckoutConfigurationQueries queries, CheckoutConfigurationOptions options)
{
    public async Task<bool> IsActive(Guid cameraId, Guid? zoneId, CancellationToken ct)
        => (await ActiveConfigurations(cameraId, zoneId, ct)).Count > 0;

    internal async Task<List<MonitoringConfiguration>> ActiveConfigurations(Guid cameraId, Guid? zoneId, CancellationToken ct)
    {
        var zones = (await EnabledMappings(cameraId, zoneId, ct)).Select(source => source.ZoneId).ToHashSet();
        return zones.Count == 0 ? [] : await store.List<MonitoringConfiguration>(c => zones.Contains(c.ZoneId) && c.Status == "ACTIVE", ct);
    }

    internal async Task<CameraCheckoutMapping[]> MappingsForPreview(Guid cameraId, Guid zoneId, CancellationToken ct)
        => (await EnabledMappings(cameraId, zoneId, ct)).Select(source => source.Mapping).ToArray();

    private async Task<List<(CameraCheckoutMapping Mapping, Guid ZoneId)>> EnabledMappings(Guid cameraId, Guid? zoneId, CancellationToken ct)
    {
        if (!await schema.Ready(ct)) return []; // No checkout capability on pre-V17 environments.
        var mappings = await queries.MappingsByCamera(cameraId, options.MaxMappingsPerCamera, ct);
        if (mappings.Count > options.MaxMappingsPerCamera)
            throw new ApplicationException("CHECKOUT_MAPPING_LIMIT", "Camera checkout mapping capacity exceeded.", 503);
        var sources = new List<(CameraCheckoutMapping, Guid)>();
        foreach (var mapping in mappings.Where(m => m.IsEnabled))
        {
            var counter = UseCase.Found(await store.Find<CheckoutCounter>(mapping.CheckoutCounterId, ct));
            if (counter.IsEnabled && (zoneId is null || zoneId == counter.ZoneId)) sources.Add((mapping, counter.ZoneId));
        }
        return sources;
    }

    public async Task EnsureCameraEditable(Guid cameraId, CancellationToken ct)
    {
        if (await IsActive(cameraId, null, ct))
            throw new ApplicationException("MONITORING_ACTIVE", "Deactivate checkout monitoring before replacing its camera source.");
    }

    public async Task EnsureZoneTypeChange(Zone zone, string? newType, CancellationToken ct)
    {
        if (zone.ZoneType == newType || newType == "CHECKOUT" || !await schema.Ready(ct)) return;
        if ((await queries.CountersByZone(zone.ZoneId, 1, ct)).Count > 0)
            throw new ApplicationException("CHECKOUT_ZONE_REQUIRED", "Remove checkout counters before changing the zone type.");
    }
}
