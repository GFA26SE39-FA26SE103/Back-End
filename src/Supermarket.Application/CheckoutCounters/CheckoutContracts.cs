using System.ComponentModel.DataAnnotations;
using Supermarket.Domain;

namespace Supermarket.Application;

public sealed record CheckoutCounterRequest(
    [Required, MaxLength(50)] string Code,
    [Required, MaxLength(100)] string Name,
    bool IsEnabled = false,
    DateTime? ExpectedUpdatedAt = null);

public sealed record CheckoutMappingRequest(
    [Required, MinLength(3), MaxLength(1000)] Point[] LaneRoi,
    [Required, MinLength(3), MaxLength(1000)] Point[] StaffRoi,
    bool IsEnabled = false,
    bool IsQueueSource = false,
    DateTime? ExpectedUpdatedAt = null,
    Point[]? ServiceRoi = null);

public sealed record CheckoutCounterView(Guid CheckoutCounterId, Guid ZoneId, string Code, string Name,
    bool IsEnabled, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record CheckoutMappingView(Guid MappingId, Guid CameraId, Guid CheckoutCounterId,
    Point[] LaneRoi, Point[] StaffRoi, bool IsEnabled, bool IsQueueSource, DateTime CreatedAt, DateTime UpdatedAt,
    Point[]? ServiceRoi = null);

public sealed class CheckoutConfigurationOptions
{
    public int MaxCountersPerZone { get; set; } = 128;
    public int MaxMappingsPerCounter { get; set; } = 16;
    public int MaxMappingsPerCamera { get; set; } = 128;

    // Leave room for the overflow sentinel row (Take(limit + 1)). ValidateOnStart in the host, too.
    public bool IsValid() => MaxCountersPerZone is > 0 and < int.MaxValue
        && MaxMappingsPerCounter is > 0 and < int.MaxValue
        && MaxMappingsPerCamera is > 0 and < int.MaxValue;
}

public interface ICheckoutSchema
{
    Task<bool> Ready(CancellationToken ct);
}

/// <summary>Return at most limit + 1 scoped rows; the extra row detects configuration overflow.</summary>
public interface ICheckoutConfigurationQueries
{
    Task<List<CheckoutCounter>> CountersByZone(Guid zoneId, int limit, CancellationToken ct);
    Task<List<CameraCheckoutMapping>> MappingsByCamera(Guid cameraId, int limit, CancellationToken ct);
    Task<List<CameraCheckoutMapping>> MappingsByCounter(Guid counterId, int limit, CancellationToken ct);
}
