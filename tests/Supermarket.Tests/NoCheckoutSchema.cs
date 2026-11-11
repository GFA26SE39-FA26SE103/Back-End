using Supermarket.Application;
using Supermarket.Domain;

namespace Supermarket.Tests;

// Legacy in-memory API factories must never query the machine's configured SQL server.
internal sealed class NoCheckoutSchema : ICheckoutSchema
{
    public Task<bool> Ready(CancellationToken ct) => Task.FromResult(false);
}

// Legacy in-memory API factories must not resolve the EF-backed checkout queries.
internal sealed class NoCheckoutConfigurationQueries : ICheckoutConfigurationQueries
{
    public Task<List<CheckoutCounter>> CountersByZone(Guid zoneId, int limit, CancellationToken ct) =>
        Task.FromResult(new List<CheckoutCounter>());

    public Task<List<CameraCheckoutMapping>> MappingsByCamera(Guid cameraId, int limit, CancellationToken ct) =>
        Task.FromResult(new List<CameraCheckoutMapping>());

    public Task<List<CameraCheckoutMapping>> MappingsByCounter(Guid counterId, int limit, CancellationToken ct) =>
        Task.FromResult(new List<CameraCheckoutMapping>());
}
