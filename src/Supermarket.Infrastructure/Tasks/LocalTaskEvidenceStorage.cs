using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Infrastructure.Tasks;

public sealed class TaskEvidenceOptions
{
    /// <summary>Local or Cloudinary. Empty follows FloorPlan:Provider, so a deployment that keeps floor plans in Cloudinary keeps evidence there too.</summary>
    public string Provider { get; set; } = "";
    /// <summary>Local photos are kept per Task under this directory; mount it on a persistent volume when Provider is Local.</summary>
    public string Root { get; set; } = ".local/task-evidence";
    /// <summary>Cloudinary asset folder; the account credentials are the shared Cloudinary section.</summary>
    public string CloudinaryFolder { get; set; } = "fa26se103/task-evidence";
    public long MaxBytes { get; set; } = 10L * 1024 * 1024;
}

/// <summary>JPEG/PNG evidence photos, validated by declared type, size and file signature (Report 3 §3.9.5).</summary>
internal static class EvidenceContent
{
    public sealed record Format(string Extension, string ContentType, byte[] Signature);
    public static readonly Format[] Formats = [
        new(".jpg", "image/jpeg", [0xff, 0xd8, 0xff]),
        new(".png", "image/png", [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])];

    public static Format Validate(string contentType, long length, long max)
    {
        if (length <= 0 || length > max) throw TooLarge(max);
        return Formats.SingleOrDefault(f => f.ContentType.Equals(contentType, StringComparison.OrdinalIgnoreCase))
            ?? throw new AppError("EVIDENCE_FORMAT_INVALID", "Only JPEG or PNG photos are supported.", 422);
    }

    public static Format? ForExtension(string extension) => Formats.SingleOrDefault(f => f.Extension == extension.ToLowerInvariant());

    /// <summary>Reads the whole upload, enforcing the declared length, the limit and the file signature.</summary>
    public static async Task<MemoryStream> Read(Stream content, Format format, long length, long max, CancellationToken ct)
    {
        var bytes = new MemoryStream();
        try
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await content.ReadAsync(buffer, ct)) > 0)
            {
                if (bytes.Length + read > max) throw TooLarge(max);
                bytes.Write(buffer, 0, read);
            }
            if (bytes.Length != length) throw new AppError("EVIDENCE_UPLOAD_INCOMPLETE", "The photo upload was incomplete. Try again.", 422);
            CheckSignature(bytes, format);
            bytes.Position = 0;
            return bytes;
        }
        catch
        {
            await bytes.DisposeAsync();
            throw;
        }
    }

    public static void CheckSignature(MemoryStream bytes, Format format)
    {
        if (bytes.Length < format.Signature.Length || !bytes.GetBuffer().AsSpan(0, format.Signature.Length).SequenceEqual(format.Signature))
            throw new AppError("EVIDENCE_FORMAT_INVALID", "The photo content does not match its declared format.", 422);
    }

    private static AppError TooLarge(long max) => new("EVIDENCE_SIZE_INVALID", $"Choose a photo of at most {max / 1024 / 1024} MB.", 422);
}

public sealed class LocalTaskEvidenceStorage(IOptions<TaskEvidenceOptions> options) : ITaskEvidenceStorage
{
    public async Task<StoredEvidence> Save(Guid taskId, Stream content, string filename, string contentType, long length, CancellationToken ct)
    {
        var format = EvidenceContent.Validate(contentType, length, options.Value.MaxBytes);
        await using var bytes = await EvidenceContent.Read(content, format, length, options.Value.MaxBytes, ct);
        var root = TaskRoot(taskId);
        Directory.CreateDirectory(root);
        var token = Guid.NewGuid().ToString("N") + format.Extension;
        var path = Path.Combine(root, token);
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await bytes.CopyToAsync(output, ct);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
        return new(token, format.ContentType);
    }

    public Task<EvidenceFile?> Open(Guid taskId, string token, CancellationToken ct)
    {
        var path = SafePath(taskId, token);
        if (path is null || !File.Exists(path)) return Task.FromResult<EvidenceFile?>(null);
        var type = EvidenceContent.ForExtension(Path.GetExtension(path))!.ContentType;
        return Task.FromResult<EvidenceFile?>(new(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read), type));
    }

    public Task Delete(Guid taskId, string token, CancellationToken ct)
    {
        var path = SafePath(taskId, token);
        if (path is not null) File.Delete(path);
        return Task.CompletedTask;
    }

    private string TaskRoot(Guid taskId) => Path.Combine(Path.GetFullPath(options.Value.Root), taskId.ToString("N"));

    private string? SafePath(Guid taskId, string token)
    {
        if (Path.GetFileName(token) != token || token.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        if (EvidenceContent.ForExtension(Path.GetExtension(token)) is null || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(token), "N", out _)) return null;
        return Path.Combine(TaskRoot(taskId), token);
    }
}

/// <summary>Picks the configured store for new photos and the right one for each stored token; removing an old photo is best-effort.</summary>
public sealed class TaskEvidenceStorage(LocalTaskEvidenceStorage local, CloudinaryTaskEvidenceStorage cloud,
    IOptions<TaskEvidenceOptions> options, ILogger<TaskEvidenceStorage> logger) : ITaskEvidenceStorage
{
    public Task<StoredEvidence> Save(Guid taskId, Stream content, string filename, string contentType, long length, CancellationToken ct)
        => Selected.Save(taskId, content, filename, contentType, length, ct);
    public Task<EvidenceFile?> Open(Guid taskId, string token, CancellationToken ct) => Storage(token).Open(taskId, token, ct);
    public async Task Delete(Guid taskId, string token, CancellationToken ct)
    {
        try { await Storage(token).Delete(taskId, token, ct); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException or AppError)
        {
            // Never log upstream bodies, credentials or signed URLs. The evidence row is authoritative.
            logger.LogWarning("Task evidence cleanup failed for task {TaskId}. Inspect storage for an unreferenced photo.", taskId);
        }
    }
    private ITaskEvidenceStorage Selected => options.Value.Provider.Equals("Cloudinary", StringComparison.OrdinalIgnoreCase) ? cloud : local;
    private ITaskEvidenceStorage Storage(string token) => CloudinaryTaskEvidenceStorage.IsCloudinary(token) ? cloud : local;
}
