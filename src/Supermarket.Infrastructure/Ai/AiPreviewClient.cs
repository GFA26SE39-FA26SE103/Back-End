using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Domain;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Infrastructure.Ai;

public sealed class AiPreviewClient(
    HttpClient http,
    IOptions<AiPreviewOptions> configured,
    ICredentialProtector secrets) : IAiPreviewClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };
    private static readonly HashSet<string> StableCodes =
    [
        "AI_PREVIEW_NOT_RUNNING",
        "AI_SESSION_CAPACITY",
        "AI_FRAME_NOT_READY",
        "AI_REQUEST_INVALID",
        "AI_SERVICE_UNAUTHORIZED",
        "AI_PREVIEW_FAILED"
    ];
    private readonly AiPreviewOptions options = configured.Value;

    public async Task<AiPreviewStatusView> Start(CameraConnection connection, CancellationToken ct, decimal? confidence = null)
    {
        using (var health = await Send(new HttpRequestMessage(HttpMethod.Get, "/health"), ct))
        {
            if (!health.IsSuccessStatusCode)
                throw new AppError("AI_SERVICE_UNAVAILABLE", "AI preview service is unavailable.", 503);
        }
        var body = new StartRequest(
            connection.StreamUri,
            connection.SourceType,
            connection.Username,
            connection.CredentialSecretRef is null ? null : secrets.Unprotect(connection.CredentialSecretRef),
            options.Model,
            options.Tracker,
            options.Classes,
            confidence ?? options.Confidence,
            options.Device,
            options.Half);
        return await SendStatus(HttpMethod.Post, $"/sessions/{connection.CameraId}/start", body, ct);
    }

    public Task<AiPreviewStatusView> Status(Guid cameraId, CancellationToken ct)
        => SendStatus(HttpMethod.Get, $"/sessions/{cameraId}/status", null, ct);

    public async Task<PreviewFrame> Frame(Guid cameraId, CancellationToken ct)
    {
        using var response = await Send(new HttpRequestMessage(HttpMethod.Get, $"/sessions/{cameraId}/frame"), ct);
        if (!response.IsSuccessStatusCode)
            throw await Error(response, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length == 0 || bytes.Length > 8 * 1024 * 1024)
            throw new AppError("AI_FRAME_INVALID", "AI preview returned an invalid frame.", 503);
        return new PreviewFrame(bytes, response.Content.Headers.ContentType?.MediaType ?? "image/jpeg");
    }

    public Task<AiPreviewStatusView> Stop(Guid cameraId, CancellationToken ct)
        => SendStatus(HttpMethod.Delete, $"/sessions/{cameraId}", null, ct);

    private async Task<AiPreviewStatusView> SendStatus(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: Json);
        using var response = await Send(request, ct);
        if (!response.IsSuccessStatusCode)
            throw await Error(response, ct);
        var dto = await response.Content.ReadFromJsonAsync<StatusResponse>(Json, ct)
            ?? throw new AppError("AI_SERVICE_INVALID_RESPONSE", "AI preview service returned an invalid response.", 503);
        return new AiPreviewStatusView(dto.CameraId, dto.State, dto.StartedAt, dto.UpdatedAt, dto.FrameSequence, dto.ErrorCode);
    }

    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(options.InternalServiceKey))
            request.Headers.TryAddWithoutValidation("X-AI-Service-Key", options.InternalServiceKey);
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AppError("AI_SERVICE_UNAVAILABLE", "AI preview service timed out.", 503);
        }
        catch (HttpRequestException)
        {
            throw new AppError("AI_SERVICE_UNAVAILABLE", "AI preview service is unavailable.", 503);
        }
    }

    private static async Task<AppError> Error(HttpResponseMessage response, CancellationToken ct)
    {
        var code = response.StatusCode switch
        {
            HttpStatusCode.Conflict => "AI_PREVIEW_NOT_RUNNING",
            HttpStatusCode.ServiceUnavailable => "AI_FRAME_NOT_READY",
            HttpStatusCode.UnprocessableEntity => "AI_REQUEST_INVALID",
            _ => "AI_PREVIEW_FAILED"
        };
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (document.RootElement.TryGetProperty("code", out var value) && value.GetString() is { } supplied && StableCodes.Contains(supplied))
                code = supplied;
        }
        catch (JsonException)
        {
        }
        var message = code switch
        {
            "AI_PREVIEW_NOT_RUNNING" => "AI preview is not running.",
            "AI_FRAME_NOT_READY" => "AI preview frame is not ready.",
            "AI_SESSION_CAPACITY" => "Another AI preview session is already using the GPU.",
            "AI_REQUEST_INVALID" => "AI preview request is invalid.",
            _ => "AI preview failed."
        };
        var status = response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.ServiceUnavailable or HttpStatusCode.UnprocessableEntity
            ? (int)response.StatusCode
            : 503;
        return new AppError(code, message, status);
    }

    private sealed record StartRequest(
        string StreamUrl,
        string SourceType,
        string? Username,
        string? Password,
        string Model,
        string Tracker,
        int[] Classes,
        decimal Confidence,
        string Device,
        bool Half);

    private sealed record StatusResponse(
        Guid CameraId,
        string State,
        DateTime? StartedAt,
        DateTime UpdatedAt,
        long FrameSequence,
        string? ErrorCode);
}
