using Microsoft.Extensions.Options;
using Supermarket.Application;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Infrastructure.FloorPlans;

public sealed class FloorPlanOptions
{
    public string Provider { get; set; } = "Local";
    public string Root { get; set; } = ".local/floor-plans";
    public long MaxBytes { get; set; } = 20L * 1024 * 1024;
}

public sealed class LocalFloorPlanStorage(IOptions<FloorPlanOptions> options) : IFloorPlanStorage
{
    public async Task<StoredFloorPlan> Save(Guid floorId, Stream content, string filename, string contentType, long length, CancellationToken ct)
    {
        var format = FloorPlanContent.Validate(filename, contentType, length, options.Value.MaxBytes);

        var root = FloorRoot(floorId);
        Directory.CreateDirectory(root);
        var token = Guid.NewGuid().ToString("N") + format.Extension;
        var path = Path.Combine(root, token);
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > options.Value.MaxBytes)
                        throw new AppError("FLOOR_MAP_SIZE_INVALID", "The floor plan exceeded the configured upload limit.", 422);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                if (total != length)
                    throw new AppError("FLOOR_MAP_SIZE_INVALID", "The floor-plan upload was incomplete.", 422);
            }

            var header = new byte[Math.Max(24, format.Signature.Length)];
            int headerLength;
            await using (var input = File.OpenRead(path))
                headerLength = await input.ReadAsync(header, ct);
            var (width, height) = FloorPlanContent.Dimensions(header.AsSpan(0, headerLength), format);
            return new(token, format.ContentType, width, height);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    public Task<FloorPlanFile?> Open(Guid floorId, string token, CancellationToken ct)
    {
        var path = SafePath(floorId, token);
        if (path is null || !File.Exists(path)) return Task.FromResult<FloorPlanFile?>(null);
        var type = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".pdf" => "application/pdf",
            _ => throw new InvalidOperationException("Unsupported stored floor-plan format.")
        };
        return Task.FromResult<FloorPlanFile?>(new(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read), type));
    }

    public Task Delete(Guid floorId, string token, CancellationToken ct)
    {
        var path = SafePath(floorId, token);
        if (path is not null) File.Delete(path);
        return Task.CompletedTask;
    }

    private string FloorRoot(Guid floorId) => Path.Combine(Path.GetFullPath(options.Value.Root), floorId.ToString("N"));

    private string? SafePath(Guid floorId, string token)
    {
        if (Path.GetFileName(token) != token || token.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        var extension = Path.GetExtension(token).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".pdf")
            || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(token), "N", out _)) return null;
        return Path.Combine(FloorRoot(floorId), token);
    }
}
