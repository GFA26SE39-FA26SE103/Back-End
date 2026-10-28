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

/// <summary>
/// Evidence photos in the shared Cloudinary product environment, the same way floor plans are kept: signed Upload API
/// calls, <c>authenticated</c> delivery and downloads through the backend only, so no public photo URL exists.
/// </summary>
public sealed class CloudinaryTaskEvidenceStorage(HttpClient http, IOptions<CloudinaryOptions> cloudinary, IOptions<TaskEvidenceOptions> options, IClock clock)
    : ITaskEvidenceStorage
{
    private const string Prefix = "cld1:";
    public static bool IsCloudinary(string token) => token.StartsWith(Prefix, StringComparison.Ordinal);

    public async Task<StoredEvidence> Save(Guid taskId, Stream content, string filename, string contentType, long length, CancellationToken ct)
    {
        var max = options.Value.MaxBytes;
        var format = EvidenceContent.Validate(contentType, length, max);
        await using var bytes = await EvidenceContent.Read(content, format, length, max, ct);
        var (config, folder) = Config();
        var publicId = $"{folder}/{taskId:N}/{Guid.NewGuid():N}";
        var fields = Signed(new() { ["public_id"] = publicId, ["asset_folder"] = folder, ["type"] = "authenticated", ["overwrite"] = "false" });
        using var form = new MultipartFormDataContent();
        foreach (var field in fields) form.Add(new StringContent(field.Value), field.Key);
        var file = new ByteArrayContent(bytes.GetBuffer(), 0, (int)bytes.Length);
        file.Headers.ContentType = new(format.ContentType);
        form.Add(file, "file", "evidence" + format.Extension);
        using var request = Request("image/upload", form);
        using var timeout = Timeout(ct);
        using var response = await Send(request, timeout.Token, ct);
        if (!response.IsSuccessStatusCode) throw Upstream();
        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
            var root = json.RootElement;
            var assetId = Text(root, "asset_id") ?? "";
            if (!Regex.IsMatch(assetId, "\\A[0-9a-fA-F]{32}\\z")) throw Upstream();
            var token = $"{Prefix}{config.CloudName}:{taskId:N}:{assetId}:{format.Extension[1..]}";
            if (Text(root, "public_id") != publicId || Text(root, "type") != "authenticated" || Text(root, "resource_type") != "image")
            {
                // Do not keep an asset changed by the product environment's upload defaults.
                try { await Delete(taskId, token, CancellationToken.None); } catch (AppError) { }
                throw Upstream();
            }
            return new(token, format.ContentType);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { throw Upstream(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw TimedOut(); }
        catch (Exception e) when (e is IOException or HttpRequestException) { throw Upstream(); }
    }

    public async Task<EvidenceFile?> Open(Guid taskId, string token, CancellationToken ct)
    {
        var asset = Parse(taskId, token);
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
        var format = EvidenceContent.ForExtension("." + asset.Extension)!;
        MemoryStream? bytes = null;
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            bytes = new MemoryStream();
            await body.CopyToAsync(bytes, timeout.Token);
            if (bytes.Length > options.Value.MaxBytes) throw Upstream();
            EvidenceContent.CheckSignature(bytes, format);
            bytes.Position = 0;
            // Buffered before the response is disposed; MVC disposes this stream after delivery.
            return new(bytes, format.ContentType);
        }
        catch (AppError) { bytes?.Dispose(); throw Upstream(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { bytes?.Dispose(); throw TimedOut(); }
        catch (Exception e) when (e is IOException or HttpRequestException) { bytes?.Dispose(); throw Upstream(); }
        catch { bytes?.Dispose(); throw; }
    }

    public async Task Delete(Guid taskId, string token, CancellationToken ct)
    {
        var asset = Parse(taskId, token);
        if (asset is null) return;
        MatchCloud(asset);
        using var request = Request("image/destroy", new FormUrlEncodedContent(Signed(new() { ["asset_id"] = asset.AssetId, ["invalidate"] = "true" })));
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
            throw new AppError("EVIDENCE_STORAGE_NOT_CONFIGURED", "Configure Cloudinary credentials and TaskEvidence:CloudinaryFolder in backend settings.", 503);
        return (config, folder);
    }
    private void MatchCloud(Asset asset)
    {
        if (asset.CloudName != Config().Config.CloudName)
            throw new AppError("EVIDENCE_STORAGE_NOT_CONFIGURED", "The photo belongs to another Cloudinary product environment.", 503);
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
    private static AppError Upstream() => new("EVIDENCE_STORAGE_UNAVAILABLE", "Cloudinary could not store or return the photo. Retry; if it keeps failing, check backend Cloudinary settings.", 502);
    private static AppError TimedOut() => new("EVIDENCE_STORAGE_TIMEOUT", "Cloudinary did not answer in time. Retry the photo upload.", 504);
    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    private sealed record Asset(string CloudName, string AssetId, string Extension);
    private static Asset? Parse(Guid taskId, string token)
    {
        if (!IsCloudinary(token) || token.Length > 300) return null;
        var parts = token.Split(':');
        if (parts.Length != 5 || !Regex.IsMatch(parts[1], "\\A[a-zA-Z0-9_-]{1,100}\\z")
            || !Guid.TryParseExact(parts[2], "N", out var owner) || owner != taskId
            || !Regex.IsMatch(parts[3], "\\A[0-9a-fA-F]{32}\\z") || parts[4] is not ("png" or "jpg")) return null;
        return new(parts[1], parts[3], parts[4]);
    }
}
