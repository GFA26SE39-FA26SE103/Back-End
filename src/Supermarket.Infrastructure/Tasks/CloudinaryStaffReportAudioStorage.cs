using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Infrastructure.FloorPlans;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Infrastructure.Tasks;

/// <summary>Audio formats a phone records for a Staff Report voice note, recognised by their file signature.</summary>
internal static class AudioContent
{
    public sealed record Format(string Extension, string ContentType);
    public static readonly Format M4a = new(".m4a", "audio/mp4"), Aac = new(".aac", "audio/aac"), Mp3 = new(".mp3", "audio/mpeg"),
        Wav = new(".wav", "audio/wav"), Ogg = new(".ogg", "audio/ogg"), Webm = new(".webm", "audio/webm");
    private static readonly Format[] All = [M4a, Aac, Mp3, Wav, Ogg, Webm];

    public static Format? ForExtension(string extension) => All.SingleOrDefault(f => f.Extension == extension.ToLowerInvariant());

    /// <summary>The declared content type is not trusted; the bytes decide the format.</summary>
    public static Format? Detect(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 12 && b[4..8].SequenceEqual("ftyp"u8)) return M4a;
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WAVE"u8)) return Wav;
        if (b.Length >= 4 && b[..4].SequenceEqual("OggS"u8)) return Ogg;
        if (b.Length >= 4 && b[0] == 0x1A && b[1] == 0x45 && b[2] == 0xDF && b[3] == 0xA3) return Webm;
        if (b.Length >= 3 && b[..3].SequenceEqual("ID3"u8)) return Mp3;
        if (b.Length >= 2 && b[0] == 0xFF && (b[1] & 0xF6) == 0xF0) return Aac;
        if (b.Length >= 2 && b[0] == 0xFF && (b[1] & 0xE0) == 0xE0) return Mp3;
        return null;
    }

    public static async Task<(MemoryStream Bytes, Format Format)> Read(Stream content, long length, long max, CancellationToken ct)
    {
        if (length <= 0 || length > max) throw new AppError("REPORT_VOICE_SIZE", "The voice note must be at most 10 MB.", 422);
        var bytes = new MemoryStream();
        try
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await content.ReadAsync(buffer, ct)) > 0)
            {
                if (bytes.Length + read > max) throw new AppError("REPORT_VOICE_SIZE", "The voice note must be at most 10 MB.", 422);
                bytes.Write(buffer, 0, read);
            }
            var format = Detect(bytes.GetBuffer().AsSpan(0, (int)Math.Min(bytes.Length, 16)))
                ?? throw new AppError("REPORT_VOICE_FORMAT", "Record the voice note as M4A/AAC, MP3, WAV, OGG or WEBM.", 422);
            bytes.Position = 0;
            return (bytes, format);
        }
        catch { await bytes.DisposeAsync(); throw; }
    }
}

/// <summary>
/// Staff Report voice notes in the shared Cloudinary environment. Cloudinary keeps audio as the <c>video</c> resource type;
/// uploads are signed, delivery is <c>authenticated</c> and downloads go through the backend only, like report photos.
/// </summary>
public sealed class CloudinaryStaffReportAudioStorage(HttpClient http, IOptions<CloudinaryOptions> cloudinary, IOptions<StaffReportMediaOptions> options, IClock clock)
    : IStaffReportAudioStorage
{
    private const string Prefix = "cla1:";

    public async Task<StoredEvidence> Save(Guid reportId, Stream content, string filename, string contentType, long length, CancellationToken ct)
    {
        var (bytes, format) = await AudioContent.Read(content, length, StaffReportWork.MaxVoiceBytes, ct);
        await using var _ = bytes;
        var (config, folder) = Config();
        var publicId = $"{folder}/{reportId:N}/{Guid.NewGuid():N}";
        var fields = Signed(new() { ["public_id"] = publicId, ["asset_folder"] = folder, ["type"] = "authenticated", ["overwrite"] = "false" });
        using var form = new MultipartFormDataContent();
        foreach (var field in fields) form.Add(new StringContent(field.Value), field.Key);
        var file = new ByteArrayContent(bytes.GetBuffer(), 0, (int)bytes.Length);
        file.Headers.ContentType = new(format.ContentType);
        form.Add(file, "file", "voice" + format.Extension);
        using var request = Request("video/upload", form);
        using var timeout = Timeout(ct);
        using var response = await Send(request, timeout.Token, ct);
        if (!response.IsSuccessStatusCode) throw Upstream();
        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
            var root = json.RootElement;
            var assetId = Text(root, "asset_id") ?? "";
            if (!Regex.IsMatch(assetId, "\\A[0-9a-fA-F]{32}\\z")) throw Upstream();
            var token = $"{Prefix}{config.CloudName}:{reportId:N}:{assetId}:{format.Extension[1..]}";
            if (Text(root, "public_id") != publicId || Text(root, "type") != "authenticated" || Text(root, "resource_type") != "video")
            {
                // Do not keep an asset changed by the product environment's upload defaults.
                try { await Destroy(publicId, CancellationToken.None); } catch (AppError) { }
                throw Upstream();
            }
            return new($"{token}:{publicId.Replace('/', '|')}", format.ContentType);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { throw Upstream(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw TimedOut(); }
        catch (Exception e) when (e is IOException or HttpRequestException) { throw Upstream(); }
    }

    public async Task<EvidenceFile?> Open(Guid reportId, string token, CancellationToken ct)
    {
        var asset = Parse(reportId, token);
        if (asset is null) return null;
        MatchCloud(asset);
        using var request = Request("asset/download", new FormUrlEncodedContent(Signed(new()
        {
            ["asset_id"] = asset.AssetId,
            ["expires_at"] = new DateTimeOffset(clock.UtcNow).AddMinutes(1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
        })));
        using var timeout = Timeout(ct);
        using var response = await Send(request, timeout.Token, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw Upstream();
        var expected = AudioContent.ForExtension("." + asset.Extension)!;
        MemoryStream? bytes = null;
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            bytes = new MemoryStream();
            await body.CopyToAsync(bytes, timeout.Token);
            if (bytes.Length > StaffReportWork.MaxVoiceBytes) throw Upstream();
            if (AudioContent.Detect(bytes.GetBuffer().AsSpan(0, (int)Math.Min(bytes.Length, 16))) != expected) throw Upstream();
            bytes.Position = 0;
            // Buffered before the response is disposed; MVC disposes this stream after delivery.
            return new(bytes, expected.ContentType);
        }
        catch (AppError) { bytes?.Dispose(); throw Upstream(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { bytes?.Dispose(); throw TimedOut(); }
        catch (Exception e) when (e is IOException or HttpRequestException) { bytes?.Dispose(); throw Upstream(); }
        catch { bytes?.Dispose(); throw; }
    }

    public async Task Delete(Guid reportId, string token, CancellationToken ct)
    {
        var asset = Parse(reportId, token);
        if (asset is null) return;
        MatchCloud(asset);
        await Destroy(asset.PublicId, ct);
    }

    private async Task Destroy(string publicId, CancellationToken ct)
    {
        using var request = Request("video/destroy", new FormUrlEncodedContent(Signed(new()
        {
            ["public_id"] = publicId, ["type"] = "authenticated", ["invalidate"] = "true"
        })));
        using var timeout = Timeout(ct);
        using var response = await Send(request, timeout.Token, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return;
        if (!response.IsSuccessStatusCode) throw Upstream();
        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
            if (Text(json.RootElement, "result") is not ("ok" or "not found")) throw Upstream();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { throw Upstream(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw TimedOut(); }
        catch (Exception e) when (e is IOException or HttpRequestException) { throw Upstream(); }
    }

    private (CloudinaryOptions Config, string Folder) Config()
    {
        var config = cloudinary.Value;
        var folder = options.Value.CloudinaryFolder;
        if (!config.IsValid() || folder.Length > 100 || !Regex.IsMatch(folder, "\\A[a-zA-Z0-9_-]+(?:/[a-zA-Z0-9_-]+)*\\z"))
            throw new AppError("EVIDENCE_STORAGE_NOT_CONFIGURED", "Configure Cloudinary credentials and StaffReportMedia:CloudinaryFolder in backend settings.", 503);
        return (config, folder);
    }
    private void MatchCloud(Asset asset)
    {
        if (asset.CloudName != Config().Config.CloudName)
            throw new AppError("EVIDENCE_STORAGE_NOT_CONFIGURED", "The voice note belongs to another Cloudinary product environment.", 503);
    }
    private Dictionary<string, string> Signed(Dictionary<string, string> fields)
    {
        var config = Config().Config;
        fields["timestamp"] = new DateTimeOffset(clock.UtcNow).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var input = string.Join('&', fields.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => f.Key + "=" + f.Value)) + config.ApiSecret;
        fields["signature"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        fields["api_key"] = config.ApiKey;
        return fields;
    }
    private HttpRequestMessage Request(string action, HttpContent content)
        => new(HttpMethod.Post, $"https://api.cloudinary.com/v1_1/{Config().Config.CloudName}/{action}") { Content = content };
    private CancellationTokenSource Timeout(CancellationToken ct)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Config().Config.TimeoutSeconds));
        return timeout;
    }
    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken timeout, CancellationToken caller)
    {
        try { return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout); }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested) { throw TimedOut(); }
        catch (HttpRequestException) { throw Upstream(); }
    }
    private static AppError Upstream() => new("EVIDENCE_STORAGE_UNAVAILABLE", "Cloudinary could not store or return the voice note. Retry; if it keeps failing, check backend Cloudinary settings.", 502);
    private static AppError TimedOut() => new("EVIDENCE_STORAGE_TIMEOUT", "Cloudinary did not answer in time. Retry the voice note upload.", 504);
    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    private sealed record Asset(string CloudName, string AssetId, string Extension, string PublicId);
    /// <summary>Token: <c>cla1:{cloud}:{reportId}:{assetId}:{ext}:{publicId with '|' for '/'}</c>, bound to its report.</summary>
    private static Asset? Parse(Guid reportId, string token)
    {
        if (!token.StartsWith(Prefix, StringComparison.Ordinal) || token.Length > 400) return null;
        var parts = token.Split(':');
        if (parts.Length != 6 || !Regex.IsMatch(parts[1], "\\A[a-zA-Z0-9_-]{1,100}\\z")
            || !Guid.TryParseExact(parts[2], "N", out var owner) || owner != reportId
            || !Regex.IsMatch(parts[3], "\\A[0-9a-fA-F]{32}\\z") || AudioContent.ForExtension("." + parts[4]) is null
            || !Regex.IsMatch(parts[5], "\\A[a-zA-Z0-9_|-]{1,250}\\z")) return null;
        return new(parts[1], parts[3], parts[4], parts[5].Replace('|', '/'));
    }
}
