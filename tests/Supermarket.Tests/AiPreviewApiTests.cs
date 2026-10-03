using System.Linq.Expressions;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Supermarket.Api.Controllers;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.Ai;
using AppError = Supermarket.Application.ApplicationException;
using Xunit;

namespace Supermarket.Tests;

public sealed class AiPreviewApiTests
{
    [Fact]
    public void ControllerRequiresAdminOrOperatorAndDocumentsExpectedRoutes()
    {
        var type = typeof(AiPreviewController);
        var authorize = Assert.Single(type.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        var route = Assert.Single(type.GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>());

        Assert.Equal("ADMIN,OPERATOR", authorize.Roles);
        Assert.Equal("api/cameras/{id:guid}/ai-preview", route.Template);
        Assert.Equal("start", type.GetMethod("Start")!.GetCustomAttributes(typeof(HttpPostAttribute), true).Cast<HttpPostAttribute>().Single().Template);
        Assert.NotNull(type.GetMethod("Status")!.GetCustomAttributes(typeof(HttpGetAttribute), true).Single());
        Assert.NotNull(type.GetMethod("Frame")!.GetCustomAttributes(typeof(HttpGetAttribute), true).Single());
        Assert.Equal("frame/next", type.GetMethod("NextFrame")!.GetCustomAttributes(typeof(HttpGetAttribute), true).Cast<HttpGetAttribute>().Single().Template);
        Assert.Equal("stop", type.GetMethod("Stop")!.GetCustomAttributes(typeof(HttpPostAttribute), true).Cast<HttpPostAttribute>().Single().Template);
    }

    [Fact]
    public async Task FrameProxyReturnsJpegWithNoStore()
    {
        var camera = ReadyCamera();
        var useCase = new AiPreview(new TestStore(camera, ReadyConnection(camera.CameraId)), new AdminUser(), new StubClient());
        var controller = new AiPreviewController(useCase)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Frame(camera.CameraId, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
        Assert.Equal([0xFF, 0xD8, 0xFF, 0xD9], file.FileContents);
    }

    [Fact]
    public async Task NextFrameProxyExposesSequenceAndReturnsNoContentWhenUnchanged()
    {
        var camera = ReadyCamera();
        var useCase = new AiPreview(new TestStore(camera, ReadyConnection(camera.CameraId)), new AdminUser(), new StubClient());
        var controller = new AiPreviewController(useCase)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.NextFrame(camera.CameraId, CancellationToken.None, 1);
        Assert.IsType<FileContentResult>(result);
        Assert.Equal("2", controller.Response.Headers["X-Frame-Sequence"]);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);

        var duplicate = await controller.NextFrame(camera.CameraId, CancellationToken.None, 2);
        Assert.IsType<NoContentResult>(duplicate);
    }

    [Fact]
    public async Task InternalClientOnlyReturnsAFrameWhenUpstreamSequenceAdvances()
    {
        var cameraId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var handler = new RecordingHandler(request =>
        {
            var response = new HttpResponseMessage(request.RequestUri!.Query.Contains("after_sequence=2") ? HttpStatusCode.NoContent : HttpStatusCode.OK);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                response.Content = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xD9]);
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                response.Headers.TryAddWithoutValidation("X-Frame-Sequence", "2");
                response.Headers.TryAddWithoutValidation("X-Session-Id", sessionId.ToString());
            }
            return response;
        });
        var client = new AiPreviewClient(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8090") }, Options.Create(new AiPreviewOptions()), new TestProtector());

        var first = await client.NextFrame(cameraId, 1, sessionId, CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal(2, first.FrameSequence);
        Assert.Equal(sessionId, first.SessionId);
        Assert.Contains("after_sequence=1", handler.LastRequest!.RequestUri!.Query);
        Assert.Contains($"after_session_id={sessionId}", handler.LastRequest.RequestUri.Query);
        Assert.Null(await client.NextFrame(cameraId, 2, sessionId, CancellationToken.None));
    }

    [Fact]
    public async Task InternalClientDecryptsCredentialOnlyForStartRequest()
    {
        var cameraId = Guid.NewGuid();
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, StatusJson(cameraId, "LIVE")));
        var options = Options.Create(new AiPreviewOptions { InternalServiceKey = "service-key" });
        var client = new AiPreviewClient(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8090") }, options, new TestProtector());
        var connection = ReadyConnection(cameraId);

        var result = await client.Start(connection, CancellationToken.None);

        Assert.Equal("LIVE", result.State);
        Assert.Equal("service-key", handler.LastRequest!.Headers.GetValues("X-AI-Service-Key").Single());
        Assert.Contains("camera-password", handler.LastBody);
        Assert.Contains(connection.StreamUri, handler.LastBody);
        Assert.Contains("\"source_type\":\"LIVE\"", handler.LastBody);
        Assert.Equal(["/health", $"/sessions/{cameraId}/start"], handler.RequestPaths);
        var publicJson = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("camera-password", publicJson);
        Assert.DoesNotContain(connection.StreamUri, publicJson);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "AI_PREVIEW_NOT_RUNNING", 409)]
    [InlineData(HttpStatusCode.Conflict, "AI_SESSION_CAPACITY", 409)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "AI_FRAME_NOT_READY", 503)]
    public async Task InternalClientMapsStableErrorsWithoutLeakingUpstreamDetail(HttpStatusCode status, string code, int expectedStatus)
    {
        var handler = new RecordingHandler(_ => Json(status, JsonSerializer.Serialize(new
        {
            code,
            message = "http://viewer:camera-password@camera/video"
        })));
        var client = new AiPreviewClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8090") },
            Options.Create(new AiPreviewOptions()),
            new TestProtector());

        var error = await Assert.ThrowsAsync<AppError>(() => client.Frame(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(code, error.Code);
        Assert.Equal(expectedStatus, error.Status);
        Assert.DoesNotContain("camera-password", error.Message);
        Assert.DoesNotContain("viewer:", error.Message);
    }

    private static Camera ReadyCamera() => new() { CameraId = Guid.NewGuid(), Status = "ACTIVE" };

    private static CameraConnection ReadyConnection(Guid cameraId) => new()
    {
        ConnectionId = Guid.NewGuid(),
        CameraId = cameraId,
        SourceType = "LIVE",
        Protocol = "HTTP",
        StreamUri = "http://camera.local:8080/video",
        Username = "viewer",
        CredentialSecretRef = "protected-value",
        IsEnabled = true,
        LastTestedAt = DateTime.UtcNow,
        LastTestResult = "SUCCESS"
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string StatusJson(Guid cameraId, string state)
        => JsonSerializer.Serialize(new
        {
            camera_id = cameraId,
            state,
            started_at = "2026-10-01T00:00:00Z",
            updated_at = "2026-10-01T00:00:01Z",
            frame_sequence = 1,
            error_code = (string?)null
        });

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string LastBody { get; private set; } = "";
        public List<string> RequestPaths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            RequestPaths.Add(request.RequestUri!.AbsolutePath);
            LastBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return response(request);
        }
    }

    private sealed class TestProtector : ICredentialProtector
    {
        public string Protect(string secret) => "protected-value";
        public string Unprotect(string secret) => secret == "protected-value" ? "camera-password" : throw new InvalidOperationException();
    }

    private sealed class AdminUser : ICurrentUser
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public string Role => "ADMIN";
    }

    private sealed class TestStore(Camera camera, CameraConnection connection) : ISetupStore
    {
        public Task<T?> Find<T>(Guid id, CancellationToken ct = default) where T : Entity, new()
            => Task.FromResult(typeof(T) == typeof(Camera) && camera.CameraId == id ? (T?)(Entity)camera : null);

        public Task<List<T>> List<T>(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) where T : Entity, new()
        {
            IEnumerable<T> values = typeof(T) == typeof(CameraConnection) ? [(T)(Entity)connection] : [];
            return Task.FromResult(filter is null ? values.ToList() : values.AsQueryable().Where(filter).ToList());
        }

        public Task Add<T>(T entity, CancellationToken ct = default) where T : Entity, new() => throw new NotSupportedException();
        public Task Update<T>(T entity, CancellationToken ct = default) where T : Entity, new() => throw new NotSupportedException();
        public Task Remove<T>(T entity, CancellationToken ct = default) where T : Entity, new() => throw new NotSupportedException();
        public Task<TResult> Transaction<TResult>(Func<Task<TResult>> work, CancellationToken ct = default) => work();
    }

    private sealed class StubClient : IAiPreviewClient
    {
        public Task<AiPreviewStatusView> Start(CameraConnection connection, CancellationToken ct, decimal? confidence = null) => Task.FromResult(View(connection.CameraId, "LIVE"));
        public Task<AiPreviewStatusView> Status(Guid cameraId, CancellationToken ct) => Task.FromResult(View(cameraId, "LIVE"));
        public Task<PreviewFrame> Frame(Guid cameraId, CancellationToken ct) => Task.FromResult(new PreviewFrame([0xFF, 0xD8, 0xFF, 0xD9], "image/jpeg"));
        public Task<SequencedPreviewFrame?> NextFrame(Guid cameraId, long afterSequence, Guid? afterSessionId, CancellationToken ct)
            => Task.FromResult<SequencedPreviewFrame?>(afterSequence >= 2 ? null : new SequencedPreviewFrame([0xFF, 0xD8, 0xFF, 0xD9], "image/jpeg", 2, Guid.NewGuid()));
        public Task<AiPreviewStatusView> Stop(Guid cameraId, CancellationToken ct) => Task.FromResult(View(cameraId, "STOPPED"));
        private static AiPreviewStatusView View(Guid id, string state) => new(id, state, DateTime.UtcNow, DateTime.UtcNow, 1, null);
    }
}
