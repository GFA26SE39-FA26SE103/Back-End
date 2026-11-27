using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Supermarket.Application;

namespace Supermarket.Infrastructure.Caching;

public sealed class RedisReadCache(ILogger<RedisReadCache> log, IDistributedCache? cache = null) : IReadCache
{
    private sealed class CacheGate
    {
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public DateTime BypassUntilUtc { get; set; }
    }

    // The API runs one replica per environment. Use bounded locks to coalesce concurrent
    // reads and prevent a read started before a write from repopulating an invalidated entry.
    private readonly CacheGate[] gates = Enumerable.Range(0, 64).Select(_ => new CacheGate()).ToArray();

    public async Task<T> GetOrCreate<T>(string key, TimeSpan lifetime, Func<CancellationToken, Task<T>> load, CancellationToken ct)
        where T : class
    {
        if (cache is null) return await load(ct);

        var gate = GateFor(key);
        await gate.Lock.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow >= gate.BypassUntilUtc)
            {
                var existing = await TryRead<T>(key, ct);
                if (existing is not null) return existing;

                var value = await load(ct);
                try
                {
                    await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value),
                        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime }, ct);
                }
                catch (RedisException error)
                {
                    log.LogWarning("Redis cache write failed for {CacheKey} ({FailureType}).", key, error.GetType().Name);
                }
                return value;
            }
        }
        finally
        {
            gate.Lock.Release();
        }
        return await load(ct);
    }

    public async Task Invalidate(string key, TimeSpan lifetime)
    {
        if (cache is null) return;

        var gate = GateFor(key);
        await gate.Lock.WaitAsync();
        try
        {
            await cache.RemoveAsync(key);
        }
        catch (RedisException error)
        {
            // SQL already committed. Bypass this stripe until any old cache entry has expired.
            gate.BypassUntilUtc = DateTime.UtcNow.Add(lifetime);
            log.LogWarning("Redis cache removal failed for {CacheKey} ({FailureType}).", key, error.GetType().Name);
        }
        finally
        {
            gate.Lock.Release();
        }
    }

    private async Task<T?> TryRead<T>(string key, CancellationToken ct) where T : class
    {
        try
        {
            var bytes = await cache!.GetAsync(key, ct);
            return bytes is null ? null : JsonSerializer.Deserialize<T>(bytes);
        }
        catch (Exception error) when (error is RedisException or JsonException)
        {
            log.LogWarning("Redis cache read failed for {CacheKey} ({FailureType}).", key, error.GetType().Name);
            return null;
        }
    }

    private CacheGate GateFor(string key) => gates[(int)((uint)StringComparer.Ordinal.GetHashCode(key) % gates.Length)];
}
