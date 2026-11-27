using Supermarket.Domain;
using Store = Supermarket.Domain.Supermarket;

namespace Supermarket.Application;

internal static class FloorReadCache
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(3);

    internal static Task<Floor> Get(ISetupStore store, IReadCache? cache, Guid floorId, CancellationToken ct)
    {
        async Task<Floor> Load(CancellationToken token) => UseCase.Found(await store.Find<Floor>(floorId, token));
        return cache is null ? Load(ct) : cache.GetOrCreate(FloorKey(floorId), Lifetime, Load, ct);
    }

    internal static Task<List<Floor>> List(ISetupStore store, IReadCache? cache, Guid storeId, CancellationToken ct)
    {
        async Task<List<Floor>> Load(CancellationToken token)
        {
            UseCase.Found(await store.Find<Store>(storeId, token));
            return await store.List<Floor>(floor => floor.SupermarketId == storeId, token);
        }
        return cache is null ? Load(ct) : cache.GetOrCreate(StoreKey(storeId), Lifetime, Load, ct);
    }

    internal static Task Invalidate(IReadCache? cache, Floor floor) => cache is null ? Task.CompletedTask :
        Task.WhenAll(cache.Invalidate(FloorKey(floor.FloorId), Lifetime), cache.Invalidate(StoreKey(floor.SupermarketId), Lifetime));

    private static string FloorKey(Guid floorId) => $"floor:v1:{floorId:N}";
    private static string StoreKey(Guid storeId) => $"store-floors:v1:{storeId:N}";
}
