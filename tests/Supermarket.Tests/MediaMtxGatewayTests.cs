using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.Video;
using Xunit;

namespace Supermarket.Tests;

public sealed class MediaMtxGatewayTests
{
    [Fact]
    public async Task MissingPathIsCreatedWithProtectedSourceAndBasicAuthentication()
    {
        var handler = new QueueHandler(
            Response(HttpStatusCode.NotFound),
            Response(HttpStatusCode.OK, "{\"status\":\"ok\"}"));
        var cameraId = Guid.Parse("21df3f32-4d73-4b5d-996f-56687dd204aa");
        var connection = Connection(cameraId);
        connection.Username = "viewer";
        connection.CredentialSecretRef = "protected";
        var gateway = Gateway(handler);

        var endpoint = await gateway.EnsurePlayback(connection, default);

        Assert.Equal("WEBRTC", endpoint.Transport);
        Assert.Equal("https://stream.example.test/cam-21df3f324d734b5d996f56687dd204aa/whep", endpoint.Url);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Post, handler.Requests[1].Method);
        Assert.EndsWith("/v3/config/paths/add/cam-21df3f324d734b5d996f56687dd204aa", handler.Requests[1].Uri);
        Assert.Contains("rtsp://viewer:secret@camera.test:554/stream", handler.Requests[1].Body);
        Assert.Contains("\"sourceOnDemand\":true", handler.Requests[1].Body);
        Assert.Contains("\"rtspTransport\":\"tcp\"", handler.Requests[1].Body);
        Assert.Equal("Basic", handler.Requests[1].AuthorizationScheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("backend-control:control-secret")), handler.Requests[1].AuthorizationParameter);
    }

    [Fact]
    public async Task MatchingPathIsReusedWithoutMutation()
    {
        var source = "rtsp://camera.test:554/stream";
        var handler = new QueueHandler(Response(HttpStatusCode.OK,
            $"{{\"source\":\"{source}\",\"sourceOnDemand\":true,\"rtspTransport\":\"tcp\"}}"));

        await Gateway(handler).EnsurePlayback(Connection(Guid.NewGuid()), default);

        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
    }

    [Fact]
    public async Task ChangedSourceReplacesExistingPath()
    {
        var handler = new QueueHandler(
            Response(HttpStatusCode.OK, "{\"source\":\"rtsp://old.test/stream\",\"sourceOnDemand\":true,\"rtspTransport\":\"tcp\"}"),
            Response(HttpStatusCode.OK, "{\"status\":\"ok\"}"));

        await Gateway(handler).EnsurePlayback(Connection(Guid.NewGuid()), default);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("/v3/config/paths/replace/", handler.Requests[1].Uri);
    }

    [Fact]
    public async Task DisabledGatewayFailsWithoutCallingControlApi()
    {
        var handler = new QueueHandler();
        var options = Options.Create(new MediaGatewayOptions());
        var gateway = new MediaMtxGateway(new HttpClient(handler) { BaseAddress = new("http://gateway.test/") }, options, new Protector());

        var error = await Assert.ThrowsAsync<Supermarket.Application.ApplicationException>(() => gateway.EnsurePlayback(Connection(Guid.NewGuid()), default));

        Assert.Equal("MEDIA_GATEWAY_NOT_CONFIGURED", error.Code);
        Assert.Empty(handler.Requests);
    }

    private static MediaMtxGateway Gateway(QueueHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new("http://gateway.test/") },
        Options.Create(new MediaGatewayOptions
        {
            Enabled = true,
            PublicBaseUrl = "https://stream.example.test",
            ControlBaseUrl = "http://gateway.test",
            ControlUsername = "backend-control",
            ControlPassword = "control-secret"
        }),
        new Protector());

    private static CameraConnection Connection(Guid cameraId) => new()
    {
        CameraId = cameraId, SourceType = "LIVE", Protocol = "RTSP", StreamUri = "rtsp://camera.test:554/stream"
    };

    private static HttpResponseMessage Response(HttpStatusCode code, string? json = null) => new(code)
    {
        Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class Protector : ICredentialProtector
    {
        public string Protect(string secret) => "protected";
        public string Unprotect(string secret) => "secret";
    }

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(
                request.Method,
                request.RequestUri!.AbsoluteUri,
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));
            return responses.Dequeue();
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, string Uri, string Body, string? AuthorizationScheme, string? AuthorizationParameter);
}
