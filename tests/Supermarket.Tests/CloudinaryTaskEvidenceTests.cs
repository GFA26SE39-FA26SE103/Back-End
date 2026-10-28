using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Supermarket.Infrastructure.FloorPlans;
using Supermarket.Infrastructure.Tasks;
using AppError = Supermarket.Application.ApplicationException;
using Xunit;

namespace Supermarket.Tests;

/// <summary>Task evidence photos in Cloudinary, stored and read the same way as floor plans (signed, authenticated, backend-only).</summary>
public sealed class CloudinaryTaskEvidenceTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private static readonly byte[] Jpeg = [0xff, 0xd8, 0xff, 0xe0, 1, 2, 3];
    private const string Secret = "fake-cloud-secret-never-log-this";

    [Fact]
    public async Task PhotosAreUploadedSignedAndAuthenticatedIntoTheEvidenceFolderAndReadBack()
    {
        using var w = new World();
        var task = Guid.NewGuid();
        var saved = await w.Cloud.Save(task, new MemoryStream(Jpeg), "after.jpg", "image/jpeg", Jpeg.Length, default);

        Assert.StartsWith($"cld1:test-cloud:{task:N}:", saved.Token);
        Assert.EndsWith(":jpg", saved.Token);
        Assert.DoesNotContain(Secret, saved.Token);
        var upload = Assert.Single(w.Handler.Calls);
        Assert.Equal(("image/upload", "authenticated", "false", "fa26se103/task-evidence"), (upload.Action, upload.Fields["type"], upload.Fields["overwrite"], upload.Fields["asset_folder"]));
        Assert.StartsWith($"fa26se103/task-evidence/{task:N}/", upload.Fields["public_id"]);
        Assert.Equal(Jpeg, upload.File);

        var file = Assert.IsType<Supermarket.Application.EvidenceFile>(await w.Cloud.Open(task, saved.Token, default));
        using (file.Content) Assert.Equal(Jpeg, ((MemoryStream)file.Content).ToArray());
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Equal("asset/download", w.Handler.Calls.Last().Action);
        // A token is bound to its Task.
        Assert.Null(await w.Cloud.Open(Guid.NewGuid(), saved.Token, default));

        await w.Cloud.Delete(task, saved.Token, default);
        Assert.Empty(w.Handler.Assets);
    }

    [Fact]
    public async Task InvalidPhotosNeverReachCloudinary()
    {
        using var w = new World();
        var task = Guid.NewGuid();
        Assert.Equal("EVIDENCE_FORMAT_INVALID", (await Assert.ThrowsAsync<AppError>(() => w.Cloud.Save(task, new MemoryStream(Png), "a.gif", "image/gif", Png.Length, default))).Code);
        Assert.Equal("EVIDENCE_FORMAT_INVALID", (await Assert.ThrowsAsync<AppError>(() => w.Cloud.Save(task, new MemoryStream(Png), "a.jpg", "image/jpeg", Png.Length, default))).Code);
        Assert.Equal("EVIDENCE_UPLOAD_INCOMPLETE", (await Assert.ThrowsAsync<AppError>(() => w.Cloud.Save(task, new MemoryStream(Png), "a.png", "image/png", Png.Length + 5, default))).Code);
        w.EvidenceOptions.MaxBytes = 4;
        Assert.Equal("EVIDENCE_SIZE_INVALID", (await Assert.ThrowsAsync<AppError>(() => w.Cloud.Save(task, new MemoryStream(Png), "a.png", "image/png", Png.Length, default))).Code);
        Assert.Empty(w.Handler.Calls);
    }

    [Fact]
    public async Task MissingCredentialsAndUpstreamFailuresGiveSafeErrors()
    {
        using var w = new World();
        w.CloudOptions.ApiSecret = "";
        Assert.Equal("EVIDENCE_STORAGE_NOT_CONFIGURED", (await Assert.ThrowsAsync<AppError>(() => w.Cloud.Save(Guid.NewGuid(), new MemoryStream(Png), "a.png", "image/png", Png.Length, default))).Code);
        w.CloudOptions.ApiSecret = Secret;
        w.Handler.Failure = "bad-key";
        var error = await Assert.ThrowsAsync<AppError>(() => w.Cloud.Save(Guid.NewGuid(), new MemoryStream(Png), "a.png", "image/png", Png.Length, default));
        Assert.Equal("EVIDENCE_STORAGE_UNAVAILABLE", error.Code);
        Assert.DoesNotContain(Secret, error.Message);
    }

    [Fact]
    public async Task TheRouterStoresNewPhotosInTheConfiguredProviderAndReadsEachTokenFromItsOwnStore()
    {
        using var w = new World();
        var task = Guid.NewGuid();
        w.EvidenceOptions.Provider = "Local";
        var local = await w.Router.Save(task, new MemoryStream(Png), "a.png", "image/png", Png.Length, default);
        w.EvidenceOptions.Provider = "Cloudinary";
        var cloud = await w.Router.Save(task, new MemoryStream(Png), "b.png", "image/png", Png.Length, default);

        Assert.False(CloudinaryTaskEvidenceStorage.IsCloudinary(local.Token));
        Assert.True(CloudinaryTaskEvidenceStorage.IsCloudinary(cloud.Token));
        foreach (var token in new[] { local.Token, cloud.Token })
        {
            var file = await w.Router.Open(task, token, default);
            using (file!.Content) Assert.Equal("image/png", file.ContentType);
        }
        // Removing a replaced photo is best-effort: a Cloudinary failure does not fail the user's action.
        w.Handler.Failure = "bad-key";
        await w.Router.Delete(task, cloud.Token, default);
        await w.Router.Delete(task, local.Token, default);
        Assert.Null(await w.Router.Open(task, local.Token, default));
    }

    private sealed class World : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "task-evidence-" + Guid.NewGuid().ToString("N"));
        public CloudinaryOptions CloudOptions { get; } = new() { CloudName = "test-cloud", ApiKey = "123456", ApiSecret = Secret, TimeoutSeconds = 1 };
        public TaskEvidenceOptions EvidenceOptions { get; }
        public FakeCloud Handler { get; } = new();
        private readonly HttpClient http;
        public CloudinaryTaskEvidenceStorage Cloud { get; }
        public TaskEvidenceStorage Router { get; }

        public World()
        {
            EvidenceOptions = new() { Provider = "Cloudinary", Root = Root };
            http = new(Handler) { Timeout = Timeout.InfiniteTimeSpan };
            Cloud = new(http, Options.Create(CloudOptions), Options.Create(EvidenceOptions), new StaffTaskTests.Clock { UtcNow = DateTime.UtcNow });
            Router = new(new(Options.Create(EvidenceOptions)), Cloud, Options.Create(EvidenceOptions), NullLogger<TaskEvidenceStorage>.Instance);
        }

        public void Dispose()
        {
            http.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }

    private sealed record Call(string Action, Dictionary<string, string> Fields, byte[]? File);

    private sealed class FakeCloud : HttpMessageHandler
    {
        public List<Call> Calls { get; } = [];
        public Dictionary<string, byte[]> Assets { get; } = [];
        public string Failure { get; set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("api.cloudinary.com", request.RequestUri!.Host);
            var fields = new Dictionary<string, string>();
            byte[]? file = null;
            if (request.Content is MultipartFormDataContent multipart)
                foreach (var part in multipart)
                {
                    var name = part.Headers.ContentDisposition!.Name!.Trim('"');
                    if (name == "file") file = await part.ReadAsByteArrayAsync(ct); else fields[name] = await part.ReadAsStringAsync(ct);
                }
            else
                foreach (var part in (await request.Content!.ReadAsStringAsync(ct)).Split('&'))
                {
                    var values = part.Split('=', 2);
                    fields[Uri.UnescapeDataString(values[0])] = Uri.UnescapeDataString(values[1].Replace('+', ' '));
                }
            var signInput = string.Join('&', fields.Where(p => p.Key is not ("api_key" or "signature")).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value)) + Secret;
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(signInput))), fields["signature"]);
            var action = string.Join('/', request.RequestUri.AbsolutePath.Split('/')[3..]);
            Calls.Add(new(action, fields, file));
            if (Failure == "bad-key") return Json(new { error = new { message = "123456:" + Secret } }, 401);
            if (action == "image/upload")
            {
                var assetId = Guid.NewGuid().ToString("N");
                Assets[assetId] = file!;
                return Json(new { asset_id = assetId, public_id = fields["public_id"], type = fields["type"], resource_type = "image" });
            }
            if (action == "asset/download")
                return Assets.TryGetValue(fields["asset_id"], out var bytes) ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) } : new(HttpStatusCode.NotFound);
            if (action == "image/destroy") { Assets.Remove(fields["asset_id"]); return Json(new { result = "ok" }); }
            throw new InvalidOperationException("Unexpected Cloudinary operation.");
        }

        private static HttpResponseMessage Json(object body, int status = 200) => new((HttpStatusCode)status) { Content = JsonContent.Create(body) };
    }
}
