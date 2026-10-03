using System.Collections.Concurrent;
using Supermarket.Domain;

namespace Supermarket.Application;

public sealed record StoredFloorPlan(string Token, string ContentType, int? Width, int? Height);
public sealed record FloorPlanFile(Stream Content, string ContentType);
public sealed record FloorPlanView(Guid FloorId, string MapUrl, int? MapWidth, int? MapHeight, string ContentType, DateTime UpdatedAt);

public interface IFloorPlanStorage
{
    Task<StoredFloorPlan> Save(Guid floorId, Stream content, string filename, string contentType, long length, CancellationToken ct);
    Task<FloorPlanFile?> Open(Guid floorId, string token, CancellationToken ct);
    void Delete(Guid floorId, string token);
    void DeleteOthers(Guid floorId, string token);
}

public sealed class FloorPlanUpload(ISetupStore store, ICurrentUser current, IFloorPlanStorage files)
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> UploadGates = new();

    public async Task<FloorPlanView> Upload(Guid floorId, Stream content, string filename, string contentType,
        long length, string mapUrl, CancellationToken ct)
    {
        UseCase.Admin(current);
        var gate = UploadGates.GetOrAdd(floorId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var floor = UseCase.Found(await store.Find<Floor>(floorId, ct));
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
                files.Delete(floorId, saved.Token);
                throw;
            }
            files.DeleteOthers(floorId, saved.Token);
            return new(floor.FloorId, floor.MapAssetUrl!, floor.MapWidth, floor.MapHeight, saved.ContentType, floor.UpdatedAt);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<FloorPlanFile> Open(Guid floorId, CancellationToken ct)
    {
        UseCase.Admin(current);
        var floor = UseCase.Found(await store.Find<Floor>(floorId, ct));
        if (floor.MapAssetUrl is null)
            throw new ApplicationException("FLOOR_MAP_NOT_FOUND", "The floor has no uploaded map.", 404);
        var uri = new Uri(floor.MapAssetUrl, UriKind.Absolute);
        var token = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2 && parts[0] == "v")
            .Select(parts => Uri.UnescapeDataString(parts[1]))
            .SingleOrDefault();
        if (string.IsNullOrWhiteSpace(token))
            throw new ApplicationException("FLOOR_MAP_NOT_FOUND", "The floor map reference is invalid.", 404);
        return UseCase.Found(await files.Open(floorId, token, ct));
    }
}
