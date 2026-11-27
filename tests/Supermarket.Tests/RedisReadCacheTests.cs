using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Supermarket.Infrastructure.Caching;
using Xunit;

namespace Supermarket.Tests;

public sealed class RedisReadCacheTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task CacheReadAndWriteFailuresStillReturnTheAuthoritativeValue()
    {
        var distributed = new FaultingCache { Offline = true };
        var cache = new RedisReadCache(NullLogger<RedisReadCache>.Instance, distributed);
        int loads = 0;

        var result = await cache.GetOrCreate("floor-zones", Lifetime,
            _ => { loads++; return Task.FromResult("from SQL"); }, default);

        Assert.Equal("from SQL", result);
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task InvalidationRemovesASnapshotBeingPopulatedByAnOlderRead()
    {
        var cache = new RedisReadCache(NullLogger<RedisReadCache>.Instance, new FaultingCache());
        var loaderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLoader = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = cache.GetOrCreate("floor-zones", Lifetime, _ =>
        {
            loaderStarted.SetResult();
            return releaseLoader.Task;
        }, default);
        await loaderStarted.Task;

        var invalidation = cache.Invalidate("floor-zones", Lifetime);
        releaseLoader.SetResult("old");
        await invalidation;

        Assert.Equal("old", await read);
        Assert.Equal("new", await cache.GetOrCreate("floor-zones", Lifetime, _ => Task.FromResult("new"), default));
    }

    [Fact]
    public async Task FailedInvalidationBypassesAnOldValueAfterRedisReturns()
    {
        var distributed = new FaultingCache();
        var cache = new RedisReadCache(NullLogger<RedisReadCache>.Instance, distributed);
        await cache.GetOrCreate("floor-zones", Lifetime, _ => Task.FromResult("old"), default);
        distributed.Offline = true;

        await cache.Invalidate("floor-zones", Lifetime);
        distributed.Offline = false;

        Assert.NotNull(await distributed.GetAsync("floor-zones"));
        Assert.Equal("new", await cache.GetOrCreate("floor-zones", Lifetime, _ => Task.FromResult("new"), default));
    }

    private sealed class FaultingCache : IDistributedCache
    {
        private readonly MemoryDistributedCache memory = new(Options.Create(new MemoryDistributedCacheOptions()));
        public bool Offline { get; set; }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Offline ? Task.FromException<byte[]?>(Failure()) : memory.GetAsync(key, token);

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            Offline ? Task.FromException(Failure()) : memory.SetAsync(key, value, options, token);

        public Task RemoveAsync(string key, CancellationToken token = default) =>
            Offline ? Task.FromException(Failure()) : memory.RemoveAsync(key, token);

        public Task RefreshAsync(string key, CancellationToken token = default) => throw new NotSupportedException();
        public byte[]? Get(string key) => throw new NotSupportedException();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new NotSupportedException();
        public void Refresh(string key) => throw new NotSupportedException();
        public void Remove(string key) => throw new NotSupportedException();

        private static RedisConnectionException Failure() => new(ConnectionFailureType.UnableToConnect, "Redis unavailable.");
    }
}
