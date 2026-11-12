using Supermarket.Domain;
using Supermarket.Domain.CheckoutCounters;

namespace Supermarket.Application;

public sealed class CheckoutMonitoringOptions
{
    // Enable only after the AI deployment supports checkout protocol v2.
    public bool AiContractEnabled { get; set; }
    public int PresentSeconds { get; set; } = 15;
    public int AbsentSeconds { get; set; } = 15;
    public int MaxObservationAgeSeconds { get; set; } = 3;
    public int MaxObservationGapMs { get; set; } = 2000;
    public int MaxClockSkewSeconds { get; set; } = 1;
    public int MaxCountersPerZone { get; set; } = 128;
    public int MaxSourcesPerCounter { get; set; } = 16;
    public int MaxZones { get; set; } = 128;
    public int MaxMappingsPerCamera { get; set; } = 128;
}

public sealed record CheckoutMappingSnapshot(Guid MappingId, Guid CameraId, Guid CounterId, Guid ZoneId,
    string ZoneName, Guid ConfigId, DateTime ConfigVersion, DateTime MappingVersion,
    Point[] LaneRoi, Point[] StaffRoi, bool IsQueueSource, decimal Confidence,
    MonitoringRuleSnapshot[] Rules, string? IssueCode = null, Point[]? ServiceRoi = null);

// Protocol v2 adds these camera-source observations; protocol v1 remains unchanged.
public sealed record AiCheckoutObservation(Guid MappingId, Guid CounterId, Guid ConfigId,
    DateTime ConfigVersion, DateTime MappingVersion, bool? PersonPresent, int? QueueCount, bool Usable,
    double? MaxWaitSeconds = null);

public sealed record CheckoutCounterRuntimeView(Guid CounterId, string Presence, int? QueueCount, bool Disagreement);
public sealed record CheckoutZoneRuntimeView(Guid ZoneId, Guid ConfigId, DateTime ConfigVersion, DateTime GeneratedAt,
    string State, string? IssueCode, long? QueueCount, int? StaffedCounterCount, decimal? QueuePerStaffedCounter,
    bool NoStaffedCapacity, CheckoutCounterRuntimeView[] Counters, MonitoringRuleRuntimeView[] Rules,
    double? MaxWaitSeconds = null);

public interface ICheckoutRuntimeState { CheckoutZoneRuntimeView? Get(Guid zoneId); }
public interface ICheckoutCameraUsability { Task<bool> Ready(Guid cameraId, CancellationToken ct); }
public interface ICheckoutEventHistory { Task<DateTime?> LatestClearedAt(Guid ruleId, Guid zoneId, CancellationToken ct); }
