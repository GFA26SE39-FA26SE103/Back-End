using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Domain;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Infrastructure.Video;

public sealed class MediaGatewayOptions
{
    public bool Enabled { get; set; }
    public string ControlBaseUrl { get; set; } = "http://127.0.0.1:9997";
    public string PublicBaseUrl { get; set; } = "";
    public string ControlUsername { get; set; } = "";
    public string ControlPassword { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 5;
}

public sealed class MediaMtxGateway(
    HttpClient http,
    IOptions<MediaGatewayOptions> configured,
    ICredentialProtector secrets) : IMediaGateway
{
    private readonly MediaGatewayOptions options = configured.Value;

    public async Task<PlaybackEndpoint> EnsurePlayback(CameraConnection connection, CancellationToken ct)
    {
        if (!options.Enabled)
            throw new AppError("MEDIA_GATEWAY_NOT_CONFIGURED", "WebRTC playback is not configured.", 503);

        var path = PathFor(connection.CameraId);
        var source = SourceFor(connection);
        var desired = new PathConfiguration(source, true, "tcp");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            using var get = Request(HttpMethod.Get, $"v3/config/paths/get/{path}");
            using var currentResponse = await http.SendAsync(get, timeout.Token);
            if (currentResponse.StatusCode == HttpStatusCode.NotFound)
            {
                await Add(path, desired, timeout.Token);
            }
            else
            {
                await EnsureSuccess(currentResponse);
                var current = await currentResponse.Content.ReadFromJsonAsync<PathConfiguration>(cancellationToken: timeout.Token);
                if (current is null || current.Source != desired.Source || current.SourceOnDemand != desired.SourceOnDemand || current.RtspTransport != desired.RtspTransport)
                    await Replace(path, desired, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AppError("MEDIA_GATEWAY_TIMEOUT", "The media gateway timed out.", 504);
        }
        catch (HttpRequestException)
        {
            throw new AppError("MEDIA_GATEWAY_UNAVAILABLE", "The media gateway is unavailable.", 503);
        }

        return new("WEBRTC", PlaybackUrl(path));
    }

    private async Task Add(string path, PathConfiguration desired, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Post, $"v3/config/paths/add/{path}", desired);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            // Another request or instance can create the same deterministic path first.
            using var retry = Request(HttpMethod.Get, $"v3/config/paths/get/{path}");
            using var existing = await http.SendAsync(retry, ct);
            if (existing.IsSuccessStatusCode)
            {
                await Replace(path, desired, ct);
                return;
            }
        }
        await EnsureSuccess(response);
    }

    private async Task Replace(string path, PathConfiguration desired, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Post, $"v3/config/paths/replace/{path}", desired);
        using var response = await http.SendAsync(request, ct);
        await EnsureSuccess(response);
    }

    private HttpRequestMessage Request(HttpMethod method, string relativeUrl, object? body = null)
    {
        var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        if (!string.IsNullOrEmpty(options.ControlUsername))
        {
            var value = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ControlUsername}:{options.ControlPassword}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", value);
        }
        return request;
    }

    private static Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return Task.CompletedTask;
        // Do not include the gateway response body: it can echo source URLs or credentials.
        throw new AppError("MEDIA_GATEWAY_UNAVAILABLE", "The media gateway rejected the playback configuration.", 503);
    }

    private string SourceFor(CameraConnection connection)
    {
        var source = new UriBuilder(connection.StreamUri);
        if (connection.Username is not null)
        {
            source.UserName = connection.Username;
            source.Password = connection.CredentialSecretRef is null ? "" : secrets.Unprotect(connection.CredentialSecretRef);
        }
        return source.Uri.AbsoluteUri;
    }

    private string PlaybackUrl(string path)
    {
        var root = options.PublicBaseUrl.EndsWith('/') ? options.PublicBaseUrl : options.PublicBaseUrl + "/";
        return new Uri(new Uri(root, UriKind.Absolute), $"{path}/whep").AbsoluteUri;
    }

    internal static string PathFor(Guid cameraId) => $"cam-{cameraId:N}";

    private sealed record PathConfiguration(
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("sourceOnDemand")] bool SourceOnDemand,
        [property: JsonPropertyName("rtspTransport")] string RtspTransport);
}
