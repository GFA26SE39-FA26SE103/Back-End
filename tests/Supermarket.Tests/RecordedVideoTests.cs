using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.Video;
using AppError = Supermarket.Application.ApplicationException;
using Xunit;

namespace Supermarket.Tests;

public sealed class RecordedVideoTests
{
    [Fact]
    public async Task RecordedPlaybackStreamsRangesAndRequiresAScopedTicketOrAiKey()
    {
        var h = new Harness();
        var assetId = Guid.NewGuid();
        var root = Directory.CreateTempSubdirectory("recorded-playback-").FullName;
        var path = Path.Combine(root, $"{assetId:N}.mp4");
        var bytes = new byte[] { 0, 1, 2, 3, 4, 5 };
        await File.WriteAllBytesAsync(path, bytes);
        h.Connection.SourceType = "RECORDED";
        h.Connection.Protocol = "FILE";
        h.Connection.StreamUri = new Uri(path).AbsoluteUri;
        h.Connection.Username = null;
        h.Connection.CredentialSecretRef = null;
        try
        {
            using var factory = new UploadApiFactory(h, root, enableRemoteVideo: true);
            using var client = factory.CreateClient();
            using var launch = new HttpRequestMessage(HttpMethod.Post, $"/api/cameras/{h.Camera.CameraId}/playback");
            launch.Headers.Add("X-Test-Role", "OPERATOR");
            using var launchResponse = await client.SendAsync(launch);
            launchResponse.EnsureSuccessStatusCode();
            var playback = Assert.IsType<CameraPlaybackView>(await launchResponse.Content.ReadFromJsonAsync<CameraPlaybackView>());
            Assert.Equal("MP4", playback.Transport);
            Assert.DoesNotContain("file:", playback.Url);

            using var request = new HttpRequestMessage(HttpMethod.Get, playback.Url);
            request.Headers.Range = new RangeHeaderValue(1, 2);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(new byte[] { 1, 2 }, await response.Content.ReadAsByteArrayAsync());

            var contentPath = RecordedVideoPlayback.ContentPath(h.Camera.CameraId, assetId);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(contentPath)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(playback.Url.Replace(assetId.ToString("N"), Guid.NewGuid().ToString("N")))).StatusCode);
            using var download = new HttpRequestMessage(HttpMethod.Get, contentPath);
            download.Headers.Add("X-AI-Service-Key", "test-video-service-key");
            using var downloaded = await client.SendAsync(download);
            Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
            Assert.Equal(bytes.Length, downloaded.Content.Headers.ContentLength);
            Assert.Equal(bytes, await downloaded.Content.ReadAsByteArrayAsync());

            h.Connection.IsEnabled = false;
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(playback.Url)).StatusCode);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(root);
        }
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("STAFF", 403)]
    [InlineData("ADMIN", 200)]
    public async Task MultipartEndpointBindsFileAndEnforcesAdmin(string? role, int status)
    {
        var h = new Harness();
        var root = Path.Combine(Path.GetTempPath(), "mf01-video-http-" + Guid.NewGuid().ToString("N"));
        using var factory = new UploadApiFactory(h, root);
        using var client = factory.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new ByteArrayContent([0, 0, 0, 12, 102, 116, 121, 112, 105, 115, 111, 109]), "file", "sample.mp4");
        var response = await client.PostAsync($"/api/cameras/{h.Camera.CameraId}/recorded-video", multipart);
        Assert.Equal(status, (int)response.StatusCode);
        if (status == 200)
        {
            Assert.Equal("RECORDED", h.Connection.SourceType);
            Assert.False(h.Connection.IsEnabled);
            Assert.Single(Directory.EnumerateFiles(root));
            Assert.DoesNotContain("file:", await response.Content.ReadAsStringAsync());
            foreach (var file in Directory.EnumerateFiles(root)) File.Delete(file);
            Directory.Delete(root);
        }
        else Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task UploadReplacesSourceClearsCredentialsAndRequiresRetestWithoutChangingMappings()
    {
        var h = new Harness();
        var mapping = new CameraZoneMapping { CameraId = h.Camera.CameraId, ZoneId = Guid.NewGuid(), Status = "ACTIVE" };
        h.Store.Values.Add(mapping);
        var result = await h.Upload.Upload(h.Camera.CameraId, Stream.Null, "test.mp4", 12, default);
        Assert.Equal("RECORDED", h.Connection.SourceType);
        Assert.Equal("FILE", h.Connection.Protocol);
        Assert.False(h.Connection.IsEnabled);
        Assert.Null(h.Connection.LastTestResult);
        Assert.Null(h.Connection.LastTestedAt);
        Assert.Null(h.Connection.Username);
        Assert.Null(h.Connection.CredentialSecretRef);
        Assert.Equal("UNKNOWN", h.Camera.HealthStatus);
        Assert.Contains(mapping, h.Store.Values);
        Assert.Equal(1, h.Ai.ForceStops);
        Assert.DoesNotContain("file:", JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("STAFF", "ACTIVE", "FORBIDDEN")]
    [InlineData("ADMIN", "INACTIVE", "CAMERA_NOT_ACTIVE")]
    public async Task UploadChecksPermissionAndLifecycleBeforeSaving(string role, string status, string code)
    {
        var h = new Harness(role);
        h.Camera.Status = status;
        var error = await Assert.ThrowsAsync<AppError>(() => h.Upload.Upload(h.Camera.CameraId, Stream.Null, "test.mp4", 12, default));
        Assert.Equal(code, error.Code);
        Assert.Equal(0, h.Files.Saves);
    }

    [Fact]
    public async Task UploadRejectsSourceReplacementDuringActiveMonitoring()
    {
        var h = new Harness();
        var zone = Guid.NewGuid();
        h.Store.Values.Add(new CameraZoneMapping { CameraId = h.Camera.CameraId, ZoneId = zone, Status = "ACTIVE" });
        h.Store.Values.Add(new MonitoringConfiguration { ZoneId = zone, Status = "ACTIVE" });
        var error = await Assert.ThrowsAsync<AppError>(() => h.Upload.Upload(h.Camera.CameraId, Stream.Null, "test.mp4", 12, default));
        Assert.Equal("MONITORING_ACTIVE", error.Code);
        Assert.Equal(0, h.Files.Saves);
        Assert.Equal(0, h.Ai.ForceStops);
    }

    [Fact]
    public async Task UploadCleansNewFileWhenConnectionTransactionFails()
    {
        var h = new Harness();
        h.Store.FailWrites = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Upload.Upload(h.Camera.CameraId, Stream.Null, "test.mp4", 12, default));
        Assert.Equal(1, h.Files.Deletes);
    }

    [Fact]
    public async Task ConfigureAlsoRejectsSourceChangesWhenMonitoringIsActive()
    {
        var h = new Harness();
        var zone = Guid.NewGuid();
        h.Store.Values.Add(new CameraZoneMapping { CameraId = h.Camera.CameraId, ZoneId = zone, Status = "ACTIVE" });
        h.Store.Values.Add(new MonitoringConfiguration { ZoneId = zone, Status = "ACTIVE" });
        var setup = new CameraSetup(h.Store, new User("ADMIN"), new FakeStream(), new Secrets(), new Clock());
        var error = await Assert.ThrowsAsync<AppError>(() => setup.Configure(h.Camera.CameraId, new ConnectionRequest("LIVE", "HTTP", "http://new/video"), default));
        Assert.Equal("MONITORING_ACTIVE", error.Code);
        Assert.Equal("http://camera/video", h.Connection.StreamUri);
    }

    [Fact]
    public async Task ConfigureCanPreserveStoredPasswordWithoutReturningIt()
    {
        var h = new Harness();
        var setup = new CameraSetup(h.Store, new User("ADMIN"), new FakeStream(), new Secrets(), new Clock());

        var result = await setup.Configure(h.Camera.CameraId,
            new ConnectionRequest("LIVE", "RTSP", "rtsp://camera/new", Username: "viewer", PreservePassword: true), default);

        Assert.Equal("rtsp://camera/new", h.Connection.StreamUri);
        Assert.Equal("viewer", result.Username);
        Assert.True(result.HasCredentials);
        Assert.Equal("encrypted", h.Connection.CredentialSecretRef);
        Assert.DoesNotContain("encrypted", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task CameraCanSaveAndRemoveIndividualRoisForOneZone()
    {
        var h=new Harness();
        var zone=new Zone { ZoneId=Guid.NewGuid(),FloorId=h.Camera.FloorId,Status="ACTIVE" };
        h.Store.Values.Add(zone);
        var setup=new CameraSetup(h.Store,new User("ADMIN"),new FakeStream(),new Secrets(),new Clock());
        var request=new MappingRequest([new Point(0,0),new Point(1,0),new Point(0,1)]);
        var first=await setup.CreateRoi(h.Camera.CameraId,zone.ZoneId,request,default);
        var second=await setup.CreateRoi(h.Camera.CameraId,zone.ZoneId,request,default);
        Assert.NotEqual(first.CameraZoneId,second.CameraZoneId);
        Assert.Equal(2,(await setup.Mappings(h.Camera.CameraId,default)).Count);
        await setup.RemoveRoi(h.Camera.CameraId,first.CameraZoneId,default);
        Assert.Equal(second.CameraZoneId,Assert.Single(await setup.Mappings(h.Camera.CameraId,default)).CameraZoneId);
    }

    [Theory]
    [InlineData("test.exe", 12, "VIDEO_FORMAT_INVALID")]
    [InlineData("test.mp4", 0, "VIDEO_SIZE_INVALID")]
    [InlineData("test.mp4", 209715201, "VIDEO_SIZE_INVALID")]
    [InlineData("test.mp4", 12, "VIDEO_FORMAT_INVALID")]
    public async Task StorageRejectsInvalidUploadsWithoutLeavingFiles(string filename, long length, string code)
    {
        var root = Path.Combine(Path.GetTempPath(), "mf01-video-test-" + Guid.NewGuid().ToString("N"));
        var files = new RecordedVideoStorage(Options.Create(new VideoOptions { RecordedRoot = root }), new FakeStream());
        try
        {
            using var content = new MemoryStream(new byte[12]);
            var error = await Assert.ThrowsAsync<AppError>(() => files.Save(content, filename, length, default));
            Assert.Equal(code, error.Code);
            Assert.True(!Directory.Exists(root) || !Directory.EnumerateFiles(root).Any());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StorageUsesGeneratedNameAndCleansFilesOnDecodeFailure(bool decodeFails)
    {
        var root = Path.Combine(Path.GetTempPath(), "mf01-video-test-" + Guid.NewGuid().ToString("N"));
        var stream = new FakeStream { Fail = decodeFails };
        var files = new RecordedVideoStorage(Options.Create(new VideoOptions { RecordedRoot = root }), stream);
        try
        {
            using var content = new MemoryStream([0, 0, 0, 12, 102, 116, 121, 112, 105, 115, 111, 109]);
            if (decodeFails)
            {
                await Assert.ThrowsAsync<AppError>(() => files.Save(content, "../../untrusted.mp4", 12, default));
                Assert.Empty(Directory.EnumerateFiles(root));
            }
            else
            {
                var uri = await files.Save(content, "../../untrusted.mp4", 12, default);
                var path = new Uri(uri).LocalPath;
                Assert.Equal(root, Path.GetDirectoryName(path));
                Assert.True(Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _));
                Assert.Single(Directory.EnumerateFiles(root));
                files.Delete(uri);
            }
        }
        finally { Directory.Delete(root); }
    }

    private sealed class Harness
    {
        public Camera Camera { get; } = new() { CameraId = Guid.NewGuid(), Status = "ACTIVE", HealthStatus = "ONLINE" };
        public CameraConnection Connection { get; }
        public TestStore Store { get; } = new();
        public FakeFiles Files { get; } = new();
        public FakeAi Ai { get; } = new();
        public RecordedVideoUpload Upload { get; }
        public Harness(string role = "ADMIN")
        {
            Connection = new() { CameraId = Camera.CameraId, SourceType = "LIVE", Protocol = "HTTP", StreamUri = "http://camera/video", IsEnabled = true, LastTestResult = "SUCCESS", LastTestedAt = DateTime.UtcNow, Username = "user", CredentialSecretRef = "encrypted" };
            Store.Values.AddRange([Camera, Connection]);
            var user = new User(role);
            Upload = new(Store, user, Files, new CameraSetup(Store, user, new FakeStream(), new Secrets(), new Clock()), Ai);
        }
    }
    private sealed class User(string role) : ICurrentUser { public Guid UserId => Guid.Empty; public string Role => role; }
    private sealed class Clock : IClock { public DateTime UtcNow => DateTime.UtcNow; }
    private sealed class Secrets : ICredentialProtector { public string Protect(string value) => value; public string Unprotect(string value) => value; }
    private sealed class FakeFiles : IRecordedVideoStorage
    {
        public int Saves, Deletes;
        public Task<string> Save(Stream content, string filename, long length, CancellationToken ct) { Saves++; return Task.FromResult("file:///C:/videos/owned.mp4"); }
        public Stream? Open(string uri) => throw new NotSupportedException();
        public void Delete(string uri) => Deletes++;
    }
    private sealed class FakeStream : ICameraStream
    {
        public bool Fail;
        public Task<ProbeResult> Test(CameraConnection c, CancellationToken ct) => Task.FromResult(new ProbeResult(true, "FRAME_RECEIVED"));
        public Task<PreviewFrame> Preview(CameraConnection c, CancellationToken ct) => Fail ? throw new AppError("STREAM_UNAVAILABLE", "Invalid video.", 422) : Task.FromResult(new PreviewFrame([1], "image/jpeg"));
    }
    private sealed class FakeAi : IAiPreviewClient
    {
        public int ForceStops;
        public Task<AiPreviewStatusView> ForceStop(Guid id, CancellationToken ct) { ForceStops++; return Task.FromResult(new AiPreviewStatusView(id, "STOPPED", null, DateTime.UtcNow, 0, null)); }
        public Task<AiPreviewStatusView> Stop(Guid id, CancellationToken ct) => Task.FromResult(new AiPreviewStatusView(id, "STOPPED", null, DateTime.UtcNow, 0, null));
        public Task<AiPreviewStatusView> Start(CameraConnection c, CancellationToken ct, decimal? confidence = null) => throw new NotSupportedException();
        public Task<AiPreviewStatusView> Status(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<PreviewFrame> Frame(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<SequencedPreviewFrame?> NextFrame(Guid id, long afterSequence, Guid? afterSessionId, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class TestStore : ISetupStore
    {
        public List<Entity> Values { get; } = [];
        public bool FailWrites;
        public Task<T?> Find<T>(Guid id, CancellationToken ct = default) where T : Entity, new() => Task.FromResult(Values.OfType<T>().FirstOrDefault());
        public Task<List<T>> List<T>(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) where T : Entity, new() => Task.FromResult((filter is null ? Values.OfType<T>() : Values.OfType<T>().Where(filter.Compile())).ToList());
        public Task Add<T>(T e, CancellationToken ct = default) where T : Entity, new() { Values.Add(e); return Task.CompletedTask; }
        public Task Update<T>(T e, CancellationToken ct = default) where T : Entity, new() => FailWrites ? throw new InvalidOperationException() : Task.CompletedTask;
        public Task Remove<T>(T e, CancellationToken ct = default) where T : Entity, new() { Values.Remove(e); return Task.CompletedTask; }
        public Task<TResult> Transaction<TResult>(Func<Task<TResult>> work, CancellationToken ct = default) => work();
    }

    private sealed class UploadApiFactory(Harness h, string root, bool enableRemoteVideo = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing").ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bootstrap:Enabled"] = "false", ["CameraHealth:Enabled"] = "false"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISetupStore>();
                services.AddSingleton<ISetupStore>(h.Store);
                services.RemoveAll<ICheckoutSchema>(); services.AddSingleton<ICheckoutSchema,NoCheckoutSchema>();
                services.RemoveAll<ICheckoutConfigurationQueries>(); services.AddSingleton<ICheckoutConfigurationQueries,NoCheckoutConfigurationQueries>();
                services.RemoveAll<ICameraStream>();
                services.AddSingleton<ICameraStream>(new FakeStream());
                services.RemoveAll<IAiPreviewClient>();
                services.AddSingleton<IAiPreviewClient>(h.Ai);
                services.PostConfigure<Supermarket.Api.JwtOptions>(o => o.Key = new string('x', 48));
                services.PostConfigure<Supermarket.Infrastructure.HealthWorkerOptions>(o => o.Enabled = false);
                services.PostConfigure<VideoOptions>(o => o.RecordedRoot = root);
                if (enableRemoteVideo)
                    services.PostConfigure<Supermarket.Infrastructure.Ai.AiPreviewOptions>(o =>
                    {
                        o.RecordedVideoBaseUrl = "https://backend.example.test";
                        o.InternalServiceKey = "test-video-service-key";
                    });
                services.AddAuthentication(o => { o.DefaultAuthenticateScheme = "UploadTest"; o.DefaultChallengeScheme = "UploadTest"; })
                    .AddScheme<AuthenticationSchemeOptions, UploadTestAuth>("UploadTest", _ => { });
            });
        }
    }
    private sealed class UploadTestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            if (role.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role)], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
