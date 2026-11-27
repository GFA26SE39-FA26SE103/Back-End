using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Supermarket.Domain;

namespace Supermarket.Application;

public sealed record StoredFloorPlan(string Token, string ContentType, int? Width, int? Height);
public sealed record FloorPlanFile(Stream Content, string ContentType);
public sealed record FloorPlanView(Guid FloorId, string MapUrl, int? MapWidth, int? MapHeight, string ContentType, DateTime UpdatedAt);

public interface IFloorPlanStorage
{
    Task<StoredFloorPlan> Save(Guid floorId, Stream content, string filename, string contentType, long length, CancellationToken ct);
    Task<FloorPlanFile?> Open(Guid floorId, string token, CancellationToken ct);
    // Delete one exact asset asynchronously; the configured storage router makes cleanup best-effort.
    Task Delete(Guid floorId, string token, CancellationToken ct);
}

public sealed class FloorPlanUpload(ISetupStore store, ICurrentUser current, IFloorPlanStorage files, IReadCache? cache = null)
{
    private static readonly TimeSpan MapCacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> UploadGates = new();
    private sealed record CachedMap(byte[] Bytes, string ContentType);

    public async Task<FloorPlanView> Upload(Guid floorId, Stream content, string filename, string contentType,
        long length, string mapUrl, CancellationToken ct)
    {
        UseCase.Admin(current);
        var gate = UploadGates.GetOrAdd(floorId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var floor = UseCase.Found(await store.Find<Floor>(floorId, ct));
            var previousUrl = floor.MapAssetUrl;
            var previousWidth = floor.MapWidth;
            var previousHeight = floor.MapHeight;
            var previousUpdatedAt = floor.UpdatedAt;
            var previousToken = Token(previousUrl);
            var saved = await files.Save(floorId, content, filename, contentType, length, ct);
            try
            {
                await store.Transaction(async () =>
                {
                    floor.MapAssetUrl = mapUrl + "?v=" + Uri.EscapeDataString(saved.Token);
                    floor.MapWidth = saved.Width;
                    floor.MapHeight = saved.Height;
                    await store.Update(floor, ct);
                    return true;
                }, ct);
            }
            catch
            {
                floor.MapAssetUrl = previousUrl;
                floor.MapWidth = previousWidth;
                floor.MapHeight = previousHeight;
                floor.UpdatedAt = previousUpdatedAt;
                await files.Delete(floorId, saved.Token, CancellationToken.None);
                throw;
            }
            await FloorReadCache.Invalidate(cache, floor);
            if (previousToken is not null && previousToken != saved.Token)
                await DeleteAsset(floorId, previousToken);
            return new(floor.FloorId, floor.MapAssetUrl!, floor.MapWidth, floor.MapHeight, saved.ContentType, floor.UpdatedAt);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<FloorPlanFile> Open(Guid floorId, CancellationToken ct)
    {
        UseCase.Viewer(current);
        var floor = await FloorReadCache.Get(store, cache, floorId, ct);
        if (floor.MapAssetUrl is null)
            throw new ApplicationException("FLOOR_MAP_NOT_FOUND", "The floor has no uploaded map.", 404);
        var token = Token(floor.MapAssetUrl);
        if (string.IsNullOrWhiteSpace(token))
            throw new ApplicationException("FLOOR_MAP_NOT_FOUND", "The floor map reference is invalid.", 404);
        if (cache is null) return UseCase.Found(await files.Open(floorId, token, ct));

        var saved = await cache.GetOrCreate(MapCacheKey(floorId, token), MapCacheLifetime, async cancellation =>
        {
            var file = UseCase.Found(await files.Open(floorId, token, cancellation));
            await using var content = file.Content;
            if (content is MemoryStream { Position: 0 } memory)
                return new CachedMap(memory.ToArray(), file.ContentType);

            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellation);
            return new CachedMap(buffer.ToArray(), file.ContentType);
        }, ct);
        return new FloorPlanFile(new MemoryStream(saved.Bytes, writable: false), saved.ContentType);
    }

    public async Task DeleteStoredAsset(Floor floor)
    {
        if (Token(floor.MapAssetUrl) is { } token)
            await DeleteAsset(floor.FloorId, token);
    }

    private async Task DeleteAsset(Guid floorId, string token)
    {
        if (cache is not null) await cache.Invalidate(MapCacheKey(floorId, token), MapCacheLifetime);
        await files.Delete(floorId, token, CancellationToken.None);
    }

    private static string MapCacheKey(Guid floorId, string token) =>
        $"floor-map:v1:{floorId:N}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))}";

    private static string? Token(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var tokens = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2 && parts[0] == "v")
            .Select(parts => Uri.UnescapeDataString(parts[1]))
            .ToArray();
        return tokens.Length == 1 ? tokens[0] : null;
    }
}
