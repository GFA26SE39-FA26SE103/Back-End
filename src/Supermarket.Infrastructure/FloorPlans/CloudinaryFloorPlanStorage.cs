using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Infrastructure.FloorPlans;

public sealed class CloudinaryOptions
{
    public string CloudName { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public string Folder { get; set; } = "fa26se103/floor-plans";
    public int TimeoutSeconds { get; set; } = 30;
    public bool IsValid() => Regex.IsMatch(CloudName, "\\A[a-zA-Z0-9_-]{1,100}\\z")
        && !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ApiSecret)
        && Regex.IsMatch(Folder, "\\A[a-zA-Z0-9_-]+(?:/[a-zA-Z0-9_-]+)*\\z") && Folder.Length <= 100
        && TimeoutSeconds is > 0 and <= 120;
}

// Signed REST calls use the documented Upload API; no SDK dependency or public CDN URLs are required.
public sealed class CloudinaryFloorPlanStorage(HttpClient http, IOptions<CloudinaryOptions> options,
    IOptions<FloorPlanOptions> floorOptions, IClock clock) : IFloorPlanStorage
{
    private const string Prefix = "cld1:";
    public static bool IsCloudinary(string token) => token.StartsWith(Prefix, StringComparison.Ordinal);

    public async Task<StoredFloorPlan> Save(Guid floorId, Stream content, string filename, string contentType, long length, CancellationToken ct)
    {
        var format = FloorPlanContent.Validate(filename, contentType, length, floorOptions.Value.MaxBytes);
        using var bytes = await FloorPlanContent.Read(content, floorOptions.Value.MaxBytes, ct);
        if (bytes.Length != length)
            throw new AppError("FLOOR_MAP_SIZE_INVALID", "The floor-plan upload was incomplete.", 422);
        var (width, height) = FloorPlanContent.Dimensions(bytes.GetBuffer().AsSpan(0, (int)bytes.Length), format);
        var config = Config();
        var resourceType = format.Extension == ".pdf" ? "raw" : "image";
        var publicId = $"{config.Folder}/{floorId:N}/{Guid.NewGuid():N}" + (resourceType == "raw" ? format.Extension : "");
        var fields = Signed(new() { ["public_id"] = publicId, ["type"] = "authenticated", ["overwrite"] = "false" });
        using var form = new MultipartFormDataContent();
        foreach (var field in fields) form.Add(new StringContent(field.Value), field.Key);
        var file = new ByteArrayContent(bytes.GetBuffer(), 0, (int)bytes.Length);
        file.Headers.ContentType = new(format.ContentType);
        form.Add(file, "file", "floor" + format.Extension);
        using var request = Request(resourceType + "/upload", form);
        using var timeout = Timeout(ct);
        using var response = await Send(request, timeout.Token, ct);
        if (!response.IsSuccessStatusCode) throw Upstream();
        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
            var root = json.RootElement;
            var assetId = Text(root, "asset_id") ?? "";
            if (!Regex.IsMatch(assetId, "\\A[0-9a-fA-F]{32}\\z")) throw Upstream();
            var token = $"{Prefix}{config.CloudName}:{floorId:N}:{assetId}:{format.Extension[1..]}";
            if (Text(root, "public_id") != publicId
                || Text(root, "type") != "authenticated"
                || Text(root, "resource_type") != resourceType
                || width is not null && (Number(root, "width") != width || Number(root, "height") != height))
            {
                // Do not persist an asset transformed by a product environment's upload defaults.
                try { await Delete(floorId, token, CancellationToken.None); } catch (AppError) { }
                throw Upstream();
            }
            return new(token, format.ContentType, width, height);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw Upstream(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw TimedOut(); }
        catch (Exception e) when (e is IOException or HttpRequestException) { throw Upstream(); }
    }

    public async Task<FloorPlanFile?> Open(Guid floorId, string token, CancellationToken ct)
    {
        var asset = Parse(floorId, token);
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
        MemoryStream? bytes = null;
        try
        {
            bytes = await FloorPlanContent.Read(await response.Content.ReadAsStreamAsync(timeout.Token), floorOptions.Value.MaxBytes, timeout.Token);
            var format = FloorPlanContent.Validate("floor." + asset.Extension, asset.ContentType, bytes.Length, floorOptions.Value.MaxBytes);
            FloorPlanContent.Dimensions(bytes.GetBuffer().AsSpan(0, (int)bytes.Length), format);
            // Buffered before the response is disposed; MVC owns/disposes this stream after delivery.
            return new(bytes, format.ContentType);
        }
        catch (AppError) { bytes?.Dispose(); throw Upstream(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { bytes?.Dispose(); throw TimedOut(); }
        catch (Exception e) when (e is IOException or HttpRequestException) { bytes?.Dispose(); throw Upstream(); }
        catch { bytes?.Dispose(); throw; }
    }

    public async Task Delete(Guid floorId, string token, CancellationToken ct)
    {
        var asset = Parse(floorId, token);
        if (asset is null) return;
        MatchCloud(asset);
        var resourceType = asset.Extension == "pdf" ? "raw" : "image";
        using var request = Request(resourceType + "/destroy", new FormUrlEncodedContent(Signed(new()
        { ["asset_id"] = asset.AssetId, ["invalidate"] = "true" })));
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

    private CloudinaryOptions Config()
    {
        var config = options.Value;
        if (!config.IsValid()) throw new AppError("FLOOR_MAP_STORAGE_NOT_CONFIGURED", "Configure Cloudinary credentials, folder and timeout in backend settings.", 503);
        return config;
    }
    private void MatchCloud(Asset asset)
    {
        if (asset.CloudName != Config().CloudName)
            throw new AppError("FLOOR_MAP_STORAGE_NOT_CONFIGURED", "The floor map belongs to another Cloudinary product environment.", 503);
    }
    private Dictionary<string, string> Signed(Dictionary<string, string> fields)
    {
        var config = Config();
        fields["timestamp"] = new DateTimeOffset(clock.UtcNow).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var input = string.Join('&', fields.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => f.Key + "=" + f.Value)) + config.ApiSecret;
        fields["signature"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        fields["api_key"] = config.ApiKey;
        return fields;
    }
    private HttpRequestMessage Request(string action, HttpContent content)
        => new(HttpMethod.Post, $"https://api.cloudinary.com/v1_1/{Config().CloudName}/{action}") { Content = content };
    private CancellationTokenSource Timeout(CancellationToken ct)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Config().TimeoutSeconds));
        return timeout;
    }
    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken timeout, CancellationToken caller)
    {
        try { return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout); }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested) { throw TimedOut(); }
        catch (HttpRequestException) { throw Upstream(); }
    }
    private static AppError Upstream() => new("FLOOR_MAP_STORAGE_UNAVAILABLE", "Cloudinary could not complete the floor-plan operation. Check backend credentials, account limits and connectivity, then retry.", 502);
    private static AppError TimedOut() => new("FLOOR_MAP_STORAGE_TIMEOUT", "Cloudinary did not complete the floor-plan operation in time. Retry later.", 504);
    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
    private static int? Number(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number && field.TryGetInt32(out var value) ? value : null;

    private sealed record Asset(string CloudName, string AssetId, string Extension)
    { public string ContentType => Extension switch { "png" => "image/png", "jpg" => "image/jpeg", _ => "application/pdf" }; }
    private static Asset? Parse(Guid floorId, string token)
    {
        if (!IsCloudinary(token) || token.Length > 300) return null;
        var parts = token.Split(':');
        if (parts.Length != 5 || !Regex.IsMatch(parts[1], "\\A[a-zA-Z0-9_-]{1,100}\\z")
            || !Guid.TryParseExact(parts[2], "N", out var owner) || owner != floorId
            || !Regex.IsMatch(parts[3], "\\A[0-9a-fA-F]{32}\\z") || parts[4] is not ("png" or "jpg" or "pdf")) return null;
        return new(parts[1], parts[3], parts[4]);
    }
}
