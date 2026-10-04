using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Supermarket.Application;

namespace Supermarket.Infrastructure.Ai;

public sealed class AiFrameHealthClient(HttpClient http, IOptions<AiPreviewOptions> configured) : IFrameHealthAnalyzer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };
    private static readonly HashSet<string> SupportedIssues =
    [
        CameraHealthEventTypes.ViewBlocked,
        CameraHealthEventTypes.ViewBlurred,
        CameraHealthEventTypes.ViewFrozen,
        CameraHealthEventTypes.FrameInvalid
    ];
    private readonly AiPreviewOptions options = configured.Value;

    public async Task<FrameAnalysisResult> Analyze(PreviewFrame frame, CancellationToken ct)
    {
        if (frame.Bytes.Length is 0 or > 4 * 1024 * 1024)
            return new(true, [CameraHealthEventTypes.FrameInvalid]);
        using var content = new ByteArrayContent(frame.Bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(frame.ContentType);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/frame-health") { Content = content };
        if (!string.IsNullOrEmpty(options.InternalServiceKey))
            request.Headers.TryAddWithoutValidation("X-AI-Service-Key", options.InternalServiceKey);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return new(false, []);
            var body = await response.Content.ReadFromJsonAsync<Response>(Json, ct);
            if (body?.Issues is null)
                return new(false, []);
            return new(true, body.Issues.Where(SupportedIssues.Contains).Distinct(StringComparer.Ordinal).ToArray());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, []);
        }
        catch (HttpRequestException)
        {
            return new(false, []);
        }
        catch (JsonException)
        {
            return new(false, []);
        }
    }

    private sealed record Response(string[] Issues);
}
