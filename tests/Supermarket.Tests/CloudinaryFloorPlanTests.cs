using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.FloorPlans;
using AppError = Supermarket.Application.ApplicationException;
using Xunit;

namespace Supermarket.Tests;

public sealed class CloudinaryFloorPlanTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private const string Secret = "fake-cloud-secret-never-log-this";
    private static Dictionary<string, string?> Settings(string root) => new()
    {
        ["FloorPlan:Provider"] = "Cloudinary", ["FloorPlan:Root"] = root,
        ["Cloudinary:CloudName"] = "test-cloud", ["Cloudinary:ApiKey"] = "123456",
        ["Cloudinary:ApiSecret"] = Secret, ["Cloudinary:Folder"] = "fa26se103/floor-plans", ["Cloudinary:TimeoutSeconds"] = "1"
    };

    [Theory, InlineData("png", "image/png"), InlineData("jpeg", "image/jpeg"), InlineData("pdf", "application/pdf")]
    public async Task SignedAuthenticatedUploadAndOriginalDownloadPreserveFormats(string extension, string mime)
    {
        using var w = new World();
        var bytes = extension switch { "png" => Png, "jpeg" => new byte[] { 0xff, 0xd8, 0xff, 0xd9 }, _ => "%PDF-1.7\noriginal"u8.ToArray() };
        var saved = await w.Cloud.Save(w.Floor.FloorId, new MemoryStream(bytes), "map." + extension, mime, bytes.Length, default);
        Assert.StartsWith("cld1:test-cloud:" + w.Floor.FloorId.ToString("N"), saved.Token);
        Assert.DoesNotContain(Secret, saved.Token);
        var upload = Assert.Single(w.Handler.Calls);
        Assert.Equal(extension == "pdf" ? "raw/upload" : "image/upload", upload.Action);
        Assert.Equal("authenticated", upload.Fields["type"]);
        Assert.Equal("false", upload.Fields["overwrite"]);
        Assert.Equal("fa26se103/floor-plans", upload.Fields["asset_folder"]);
        Assert.StartsWith("fa26se103/floor-plans/" + w.Floor.FloorId.ToString("N") + "/", upload.Fields["public_id"]);
        Assert.DoesNotContain("transformation", upload.Fields.Keys);
        Assert.Equal(bytes, upload.File);
        if (extension == "pdf") Assert.EndsWith(".pdf", upload.Fields["public_id"]);
        Assert.Equal(extension == "png" ? 1 : (int?)null, saved.Width);
        Assert.Equal(extension == "png" ? 1 : (int?)null, saved.Height);
        var file = Assert.IsType<FloorPlanFile>(await w.Cloud.Open(w.Floor.FloorId, saved.Token, default));
        using (file.Content) Assert.Equal(bytes, await Read(file.Content));
        Assert.Equal(mime, file.ContentType);
        var download = w.Handler.Calls.Last();
        Assert.Equal("asset/download", download.Action);
        Assert.DoesNotContain("transformation", download.Fields.Keys);
        Assert.DoesNotContain("format", download.Fields.Keys);
        Assert.True(long.Parse(download.Fields["expires_at"], CultureInfo.InvariantCulture) > long.Parse(download.Fields["timestamp"], CultureInfo.InvariantCulture));
        await w.Cloud.Delete(w.Floor.FloorId, saved.Token, default);
        Assert.Empty(w.Handler.Assets);
    }

    [Fact]
    public async Task CustomFolderIsSentAsTheMediaLibraryFolder()
    {
        using var w = new World();
        w.CloudOptions.Folder = "demo-store/maps";
        await w.Cloud.Save(w.Floor.FloorId, new MemoryStream(Png), "map.png", "image/png", Png.Length, default);
        var upload = Assert.Single(w.Handler.Calls);
        Assert.Equal("demo-store/maps", upload.Fields["asset_folder"]);
        Assert.StartsWith("demo-store/maps/" + w.Floor.FloorId.ToString("N") + "/", upload.Fields["public_id"]);
    }

    [Theory, InlineData("empty"), InlineData("oversized"), InlineData("signature"), InlineData("extension"), InlineData("mime"), InlineData("incomplete"), InlineData("short-png")]
    public async Task InvalidUploadsNeverCallCloudinary(string state)
    {
        using var w = new World();
        var bytes = state switch { "empty" => [], "signature" => "not an image"u8.ToArray(), "short-png" => Png[..8], _ => Png };
        var length = state == "incomplete" ? bytes.Length + 1 : bytes.Length;
        w.FloorOptions.MaxBytes = state == "oversized" ? 8 : 20971520;
        var error = await Assert.ThrowsAsync<AppError>(() => w.Cloud.Save(w.Floor.FloorId, new MemoryStream(bytes), state == "extension" ? "map.txt" : "map.png", state == "mime" ? "text/plain" : "image/png", length, default));
        Assert.Equal(422, error.Status); Assert.Empty(w.Handler.Calls);
    }

    [Theory, InlineData("bad-key"), InlineData("http"), InlineData("timeout"), InlineData("malformed"), InlineData("transformed")]
    public async Task StorageFailuresAreSanitizedAndKeepThePreviousMap(string failure)
    {
        using var w = new World(); await w.Upload();
        var previousUrl = w.Floor.MapAssetUrl;
        w.Handler.Failure = failure;
        var error = await Assert.ThrowsAsync<AppError>(() => w.Upload());
        Assert.Equal(failure == "timeout" ? 504 : 502, error.Status);
        Assert.DoesNotContain(Secret, error.Message);
        Assert.DoesNotContain("123456", error.Message);
        Assert.Equal(previousUrl, w.Floor.MapAssetUrl);
        Assert.Single(w.Handler.Assets);
        w.Handler.Failure = "";
        await w.ReadCurrent();
    }

    [Fact]
    public async Task FailedDatabaseCommitCleansOnlyTheNewUploadAndRestoresTheOldReference()
    {
        using var w = new World(); await w.Upload();
        var oldUrl = w.Floor.MapAssetUrl;
        var oldAsset = Assert.Single(w.Handler.Assets).Key;
        w.FailUpdate = true;
        Assert.Equal("SAVE_FAILED", (await Assert.ThrowsAsync<AppError>(() => w.Upload())).Code);
        Assert.Equal(oldUrl, w.Floor.MapAssetUrl);
        Assert.Equal(oldAsset, Assert.Single(w.Handler.Assets).Key);
        Assert.NotEqual(oldAsset, w.Handler.Calls.Last().Fields["asset_id"]);
        await w.ReadCurrent();
    }

    [Fact]
    public async Task ReplacementDeletesOnlyItsOldReferenceAndSurvivesAStorageRestart()
    {
        using var w = new World(); await w.Upload();
        var old = Assert.Single(w.Handler.Assets).Key;
        w.Handler.Assets["cccccccccccccccccccccccccccccccc"] = Png;
        await w.Upload();
        Assert.DoesNotContain(old, w.Handler.Assets.Keys);
        Assert.Equal(2, w.Handler.Assets.Count);
        Assert.Equal(old, w.Handler.Calls.Last().Fields["asset_id"]);
        // A fresh adapter has no local file/cache knowledge; the DB reference and cloud asset suffice.
        var newCloud = new CloudinaryFloorPlanStorage(w.Http, Options.Create(w.CloudOptions), Options.Create(w.FloorOptions), new Clock());
        var file = Assert.IsType<FloorPlanFile>(await newCloud.Open(w.Floor.FloorId, Uri.UnescapeDataString(new Uri(w.Floor.MapAssetUrl!).Query[3..]), default));
        using (file.Content) Assert.Equal(Png, await Read(file.Content));
    }

    [Fact]
    public async Task FailedCleanupDoesNotTurnACommittedUploadIntoAnErrorOrExposeSecrets()
    {
        using var w = new World(); await w.Upload(); var oldUrl = w.Floor.MapAssetUrl;
        w.Handler.FailCleanup = true;
        await w.Upload();
        Assert.NotEqual(oldUrl, w.Floor.MapAssetUrl);
        Assert.Equal(2, w.Handler.Assets.Count);
        Assert.Single(w.Log.Messages); Assert.DoesNotContain(Secret, w.Log.Messages.Single());
        await w.ReadCurrent();
    }

    [Fact]
    public async Task SwitchingProviderKeepsLegacyLocalImagesReadableUntilReplacement()
    {
        using var w = new World(); w.FloorOptions.Provider = "Local"; await w.Upload();
        var oldUrl = w.Floor.MapAssetUrl;
        Assert.Single(Directory.EnumerateFiles(w.Root, "*", SearchOption.AllDirectories));
        w.FloorOptions.Provider = "Cloudinary"; await w.ReadCurrent();
        Assert.Empty(w.Handler.Calls);
        Assert.Equal(oldUrl, w.Floor.MapAssetUrl);
        await w.Upload();
        Assert.Empty(Directory.EnumerateFiles(w.Root, "*", SearchOption.AllDirectories));
        await w.ReadCurrent();
    }

    [Theory, InlineData("floor"), InlineData("malformed"), InlineData("url")]
    public async Task InvalidReferencesDoNotMakeExternalRequests(string scenario)
    {
        using var w = new World();
        var token = scenario switch {
            "floor" => $"cld1:test-cloud:{Guid.NewGuid():N}:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:png",
            "url" => "https://untrusted.example/image.png", _ => "cld1:../bad:bad:bad:exe" };
        Assert.Null(await w.Router.Open(w.Floor.FloorId, token, default));
        await w.Router.Delete(w.Floor.FloorId, token, default);
        Assert.Empty(w.Handler.Calls);
    }

    [Fact]
    public async Task MissingKeysAndDifferentCloudAreExplicitConfigurationErrors()
    {
        using var w = new World(); await w.Upload();
        var token = Uri.UnescapeDataString(new Uri(w.Floor.MapAssetUrl!).Query[3..]);
        w.CloudOptions.ApiSecret = "";
        Assert.Equal("FLOOR_MAP_STORAGE_NOT_CONFIGURED", (await Assert.ThrowsAsync<AppError>(() => w.Cloud.Open(w.Floor.FloorId, token, default))).Code);
        w.CloudOptions.ApiSecret = Secret; w.CloudOptions.CloudName = "different-cloud";
        Assert.Equal("FLOOR_MAP_STORAGE_NOT_CONFIGURED", (await Assert.ThrowsAsync<AppError>(() => w.Cloud.Open(w.Floor.FloorId, token, default))).Code);
        Assert.Single(w.Handler.Calls);
    }

    [Fact]
    public async Task MissingAssetReturnsNotFoundAndBadDownloadNeverReachesTheBrowser()
    {
        using var w = new World(); await w.Upload();
        var assetId = Assert.Single(w.Handler.Assets).Key;
        w.Handler.Assets[assetId] = "secret upstream error pretending to be an image"u8.ToArray();
        Assert.Equal(502, (await Assert.ThrowsAsync<AppError>(() => w.Setup.Open(w.Floor.FloorId, default))).Status);
        w.Handler.Assets.Remove(assetId);
        Assert.Equal(404, (await Assert.ThrowsAsync<AppError>(() => w.Setup.Open(w.Floor.FloorId, default))).Status);
    }

    [Theory, InlineData(null, 401), InlineData("STAFF", 403), InlineData("OPERATOR", 403), InlineData("MANAGER", 403), InlineData("ADMIN", 200)]
    public async Task CloudinaryUploadThroughHttpIsAdminOnly(string? role, int status)
    {
        using var w = new World(); await using var parent = new SetupOverviewTests.Factory(w.Store);
        await using var factory = parent.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(Settings(w.Root)))
            .ConfigureTestServices(services => services.AddHttpClient<CloudinaryFloorPlanStorage>().ConfigurePrimaryHttpMessageHandler(() => w.Handler)));
        using var client = factory.CreateClient(); if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        using var form = new MultipartFormDataContent(); var file = new ByteArrayContent(Png); file.Headers.ContentType = new("image/png"); form.Add(file, "file", "floor.png");
        var response = await client.PostAsync($"/api/floors/{w.Floor.FloorId}/map", form);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(status == 200 ? 1 : 0, w.Handler.Calls.Count);
        if (status == 200) {
            var result = await response.Content.ReadFromJsonAsync<FloorPlanView>(); Assert.Equal(1, result!.MapWidth);
            Assert.StartsWith($"http://localhost/api/floors/{w.Floor.FloorId}/map?v=", result.MapUrl);
            Assert.DoesNotContain(Secret, await response.Content.ReadAsStringAsync());
        }
    }

    [Theory, InlineData(null, 401), InlineData("STAFF", 403), InlineData("OPERATOR", 200), InlineData("MANAGER", 200), InlineData("ADMIN", 200)]
    public async Task CloudinaryMapReadThroughHttpPreservesViewerRolesAndNoStore(string? role, int status)
    {
        using var w = new World(); await w.Upload(); w.Handler.Calls.Clear();
        await using var parent = new SetupOverviewTests.Factory(w.Store);
        await using var factory = parent.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(Settings(w.Root)))
            .ConfigureTestServices(services => services.AddHttpClient<CloudinaryFloorPlanStorage>().ConfigurePrimaryHttpMessageHandler(() => w.Handler)));
        using var client = factory.CreateClient(); if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        var response = await client.GetAsync($"/api/floors/{w.Floor.FloorId}/map");
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(status == 200 ? 1 : 0, w.Handler.Calls.Count);
        if (status == 200) { Assert.Equal(Png, await response.Content.ReadAsByteArrayAsync()); Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType); Assert.True(response.Headers.CacheControl!.NoStore); }
    }

    [Fact]
    public void EnablingCloudinaryWithoutCredentialsFailsAtStartup()
    {
        using var w = new World(); var settings = Settings(w.Root); settings["Cloudinary:ApiSecret"] = "";
        using var parent = new SetupOverviewTests.Factory(w.Store);
        using var factory = parent.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(settings)));
        var error = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("ApiSecret", error.Message); Assert.DoesNotContain(Secret, error.Message);
    }

    private static async Task<byte[]> Read(Stream stream) { using var bytes = new MemoryStream(); await stream.CopyToAsync(bytes); return bytes.ToArray(); }
    private sealed class Clock : IClock { public DateTime UtcNow => new(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc); }
    private sealed class User : ICurrentUser { public Guid UserId => Guid.NewGuid(); public string Role => "ADMIN"; }
    private sealed class CaptureLog : ILogger<FloorPlanStorage>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? e, Func<TState, Exception?, string> format) => Messages.Add(format(state, e));
    }
    private sealed class World : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "mf01-cloud-storage-" + Guid.NewGuid().ToString("N"));
        public Floor Floor { get; } = new() { FloorId = Guid.NewGuid(), Name = "Ground floor" };
        public MonitoringSetupTests.MemoryStore Store { get; } = new();
        public CloudinaryOptions CloudOptions { get; } = new() { CloudName = "test-cloud", ApiKey = "123456", ApiSecret = Secret, TimeoutSeconds = 1 };
        public FloorPlanOptions FloorOptions { get; }
        public FakeCloud Handler { get; } = new();
        public HttpClient Http { get; }
        public CloudinaryFloorPlanStorage Cloud { get; }
        public FloorPlanStorage Router { get; }
        public FloorPlanUpload Setup { get; }
        public CaptureLog Log { get; } = new();
        public bool FailUpdate { get; set; }
        public World()
        {
            FloorOptions = new() { Provider = "Cloudinary", Root = Root };
            Http = new(Handler) { Timeout = Timeout.InfiniteTimeSpan };
            Cloud = new(Http, Options.Create(CloudOptions), Options.Create(FloorOptions), new Clock());
            Router = new(new(Options.Create(FloorOptions)), Cloud, Options.Create(FloorOptions), Log);
            Store.Values.Add(Floor);
            Setup = new(new FailingStore(Store, () => FailUpdate), new User(), Router);
        }
        public Task Upload() => Setup.Upload(Floor.FloorId, new MemoryStream(Png), "floor.png", "image/png", Png.Length, $"http://localhost/api/floors/{Floor.FloorId}/map", default);
        public async Task ReadCurrent() { var file = await Setup.Open(Floor.FloorId, default); using (file.Content) Assert.Equal(Png, await Read(file.Content)); }
        public void Dispose() { Http.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private sealed class FailingStore(MonitoringSetupTests.MemoryStore store, Func<bool> failed) : ISetupStore
    {
        public Task<T?> Find<T>(Guid id, CancellationToken ct = default) where T : Entity, new() => store.Find<T>(id, ct);
        public Task<List<T>> List<T>(System.Linq.Expressions.Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) where T : Entity, new() => store.List(filter, ct);
        public Task Add<T>(T entity, CancellationToken ct = default) where T : Entity, new() => store.Add(entity, ct);
        public Task Remove<T>(T entity, CancellationToken ct = default) where T : Entity, new() => store.Remove(entity, ct);
        public Task Update<T>(T entity, CancellationToken ct = default) where T : Entity, new() => failed() ? throw new AppError("SAVE_FAILED", "Save failed.", 500) : store.Update(entity, ct);
        public Task<T> Transaction<T>(Func<Task<T>> work, CancellationToken ct = default) => store.Transaction(work, ct);
    }
    private sealed record Call(string Action, Dictionary<string, string> Fields, byte[]? File);
    private sealed class FakeCloud : HttpMessageHandler
    {
        public List<Call> Calls { get; } = [];
        public Dictionary<string, byte[]> Assets { get; } = [];
        public string Failure { get; set; } = "";
        public bool FailCleanup { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("https", request.RequestUri!.Scheme); Assert.Equal("api.cloudinary.com", request.RequestUri.Host);
            Assert.Equal("", request.RequestUri.Query); Assert.Equal(HttpMethod.Post, request.Method);
            var fields = new Dictionary<string, string>(); byte[]? file = null;
            if (request.Content is MultipartFormDataContent multipart) foreach (var part in multipart) {
                var name = part.Headers.ContentDisposition!.Name!.Trim('"');
                if (name == "file") file = await part.ReadAsByteArrayAsync(ct); else fields[name] = await part.ReadAsStringAsync(ct);
            }
            else foreach (var part in (await request.Content!.ReadAsStringAsync(ct)).Split('&')) {
                var values = part.Split('=', 2); fields[Uri.UnescapeDataString(values[0])] = Uri.UnescapeDataString(values[1].Replace('+', ' '));
            }
            Assert.Equal("123456", fields["api_key"]);
            var signInput = string.Join('&', fields.Where(p => p.Key is not ("api_key" or "signature")).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value)) + Secret;
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(signInput))), fields["signature"]);
            Assert.DoesNotContain(Secret, string.Join('|', fields.Values));
            var action = string.Join('/', request.RequestUri.AbsolutePath.Split('/')[3..]);
            Calls.Add(new(action, fields, file));
            if (Failure == "http") throw new HttpRequestException(Secret);
            if (Failure == "timeout") { await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); }
            if (Failure == "bad-key" || FailCleanup && action.EndsWith("/destroy")) return Json(new { error = new { message = "123456:" + Secret } }, 401);
            if (action.EndsWith("/upload")) {
                if (Failure == "malformed") return new(HttpStatusCode.OK) { Content = new StringContent("123456:" + Secret) };
                var assetId = Guid.NewGuid().ToString("N"); Assets[assetId] = file!;
                return Json(new { asset_id = assetId, public_id = fields["public_id"], type = fields["type"], resource_type = action.StartsWith("raw") ? "raw" : "image", width = Failure == "transformed" ? 2 : 1, height = 1 });
            }
            if (action == "asset/download") return Assets.TryGetValue(fields["asset_id"], out var bytes) ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) } : new(HttpStatusCode.NotFound);
            if (action.EndsWith("/destroy")) { Assets.Remove(fields["asset_id"]); return Json(new { result = "ok" }); }
            throw new InvalidOperationException("Unexpected Cloudinary operation.");
        }
        private static HttpResponseMessage Json(object body, int status = 200) => new((HttpStatusCode)status) { Content = JsonContent.Create(body) };
    }
}
