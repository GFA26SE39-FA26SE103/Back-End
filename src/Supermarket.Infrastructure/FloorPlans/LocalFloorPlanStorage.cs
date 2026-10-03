using System.Buffers.Binary;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Infrastructure.FloorPlans;

public sealed class FloorPlanOptions
{
    public string Root { get; set; } = ".local/floor-plans";
    public long MaxBytes { get; set; } = 20L * 1024 * 1024;
}

public sealed class LocalFloorPlanStorage(IOptions<FloorPlanOptions> options) : IFloorPlanStorage
{
    private static readonly Dictionary<string, (string Extension, byte[] Signature)> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = (".png", [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
        ["image/jpeg"] = (".jpg", [0xff, 0xd8, 0xff]),
        ["application/pdf"] = (".pdf", "%PDF-"u8.ToArray())
    };

    public async Task<StoredFloorPlan> Save(Guid floorId, Stream content, string filename, string contentType, long length, CancellationToken ct)
    {
        if (length <= 0 || length > options.Value.MaxBytes)
            throw new AppError("FLOOR_MAP_SIZE_INVALID", $"Choose a non-empty floor plan of at most {options.Value.MaxBytes / 1024 / 1024} MB.", 422);
        if (!Supported.TryGetValue(contentType, out var format)
            || !string.Equals(Path.GetExtension(filename), format.Extension, StringComparison.OrdinalIgnoreCase)
                && !(contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
                    && Path.GetExtension(filename).Equals(".jpeg", StringComparison.OrdinalIgnoreCase)))
            throw new AppError("FLOOR_MAP_FORMAT_INVALID", "Only PNG, JPEG, or PDF floor plans are supported.", 422);

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
            if (headerLength < format.Signature.Length || !header.AsSpan(0, format.Signature.Length).SequenceEqual(format.Signature))
                throw new AppError("FLOOR_MAP_FORMAT_INVALID", "The uploaded file content does not match its declared format.", 422);

            int? width = null;
            int? height = null;
            if (contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase) && headerLength >= 24)
            {
                width = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
                height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
                if (width <= 0 || height <= 0)
                    throw new AppError("FLOOR_MAP_FORMAT_INVALID", "The PNG floor plan has invalid dimensions.", 422);
            }
            return new(token, contentType, width, height);
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

    public void Delete(Guid floorId, string token)
    {
        var path = SafePath(floorId, token);
        if (path is not null) File.Delete(path);
    }

    public void DeleteOthers(Guid floorId, string token)
    {
        var root = FloorRoot(floorId);
        if (!Directory.Exists(root)) return;
        foreach (var path in Directory.EnumerateFiles(root))
            if (!string.Equals(Path.GetFileName(path), token, StringComparison.OrdinalIgnoreCase))
                File.Delete(path);
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
