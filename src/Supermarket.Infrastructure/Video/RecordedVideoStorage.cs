using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Domain;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Infrastructure.Video;

public sealed class RecordedVideoStorage(IOptions<VideoOptions> options, ICameraStream streams) : IRecordedVideoStorage
{
    public async Task<string> Save(Stream content, string filename, long length, CancellationToken ct)
    {
        if (length <= 0 || length > RecordedVideoUpload.MaxBytes)
            throw new AppError("VIDEO_SIZE_INVALID", "Choose a non-empty video of at most 200 MB.", 422);
        if (!string.Equals(Path.GetExtension(filename), ".mp4", StringComparison.OrdinalIgnoreCase))
            throw new AppError("VIDEO_FORMAT_INVALID", "Only MP4 video is supported.", 422);
        var root = Path.GetFullPath(options.Value.RecordedRoot);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".mp4");
        var uri = new Uri(path).AbsoluteUri;
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
                    if (total > RecordedVideoUpload.MaxBytes)
                        throw new AppError("VIDEO_SIZE_INVALID", "Video exceeded the 200 MB upload limit.", 422);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                if (total != length)
                    throw new AppError("VIDEO_SIZE_INVALID", "The upload was incomplete.", 422);
            }
            await using (var input = File.OpenRead(path))
            {
                var header = new byte[12];
                if (await input.ReadAsync(header, ct) < 12 || !header.AsSpan(4, 4).SequenceEqual("ftyp"u8))
                    throw new AppError("VIDEO_FORMAT_INVALID", "The file is not an MP4 video.", 422);
            }
            // Extension/MIME alone cannot establish whether the file contains a decodable video stream.
            await streams.Preview(new CameraConnection { SourceType = "RECORDED", Protocol = "FILE", StreamUri = uri }, ct);
            return uri;
        }
        catch
        {
            Delete(uri);
            throw;
        }
    }

    public void Delete(string uri)
    {
        var path = Path.GetFullPath(new Uri(uri).LocalPath);
        var root = Path.GetFullPath(options.Value.RecordedRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _)
            && Path.GetExtension(path) == ".mp4")
            File.Delete(path);
    }
}
