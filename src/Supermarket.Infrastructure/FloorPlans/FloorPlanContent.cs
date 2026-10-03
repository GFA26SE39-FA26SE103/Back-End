using System.Buffers.Binary;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Infrastructure.FloorPlans;

internal sealed record FloorPlanFormat(string Extension, string ContentType, byte[] Signature);

internal static class FloorPlanContent
{
    private static readonly FloorPlanFormat[] Formats = [
        new(".png", "image/png", [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
        new(".jpg", "image/jpeg", [0xff, 0xd8, 0xff]),
        new(".pdf", "application/pdf", "%PDF-"u8.ToArray())];

    public static FloorPlanFormat Validate(string filename, string contentType, long length, long maxBytes)
    {
        if (length <= 0 || length > maxBytes)
            throw new AppError("FLOOR_MAP_SIZE_INVALID", $"Choose a non-empty floor plan of at most {maxBytes / 1024 / 1024} MB.", 422);
        var format = Formats.SingleOrDefault(f => f.ContentType.Equals(contentType, StringComparison.OrdinalIgnoreCase));
        var ext = Path.GetExtension(filename);
        if (format is null || !ext.Equals(format.Extension, StringComparison.OrdinalIgnoreCase)
            && !(format.Extension == ".jpg" && ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)))
            throw new AppError("FLOOR_MAP_FORMAT_INVALID", "Only PNG, JPEG, or PDF floor plans are supported.", 422);
        return format;
    }

    public static (int? Width, int? Height) Dimensions(ReadOnlySpan<byte> header, FloorPlanFormat format)
    {
        if (header.Length < format.Signature.Length || !header[..format.Signature.Length].SequenceEqual(format.Signature))
            throw new AppError("FLOOR_MAP_FORMAT_INVALID", "The uploaded file content does not match its declared format.", 422);
        if (format.Extension != ".png") return (null, null);
        if (header.Length < 24) throw new AppError("FLOOR_MAP_FORMAT_INVALID", "The PNG floor plan has an incomplete header.", 422);
        var width = BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(header.Slice(20, 4));
        if (width <= 0 || height <= 0)
            throw new AppError("FLOOR_MAP_FORMAT_INVALID", "The PNG floor plan has invalid dimensions.", 422);
        return (width, height);
    }

    public static async Task<MemoryStream> Read(Stream source, long maxBytes, CancellationToken ct)
    {
        var result = new MemoryStream();
        try
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                if (result.Length + read > maxBytes)
                    throw new AppError("FLOOR_MAP_SIZE_INVALID", "The floor plan exceeded the configured upload limit.", 422);
                await result.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            result.Position = 0;
            return result;
        }
        catch { result.Dispose(); throw; }
    }
}
