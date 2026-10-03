using System.Linq.Expressions;
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
using Xunit;

namespace Supermarket.Tests;

public sealed class FloorPlanTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Theory]
    [InlineData(null, 401)]
    [InlineData("STAFF", 403)]
    [InlineData("ADMIN", 200)]
    public async Task UploadRequiresAdminAndPersistsFloorMap(string? role, int expectedStatus)
    {
        var floor = new Floor
        {
            FloorId = Guid.NewGuid(),
            SupermarketId = Guid.NewGuid(),
            FloorNumber = 1,
            Name = "Ground floor"
        };
        var root = Path.Combine(Path.GetTempPath(), "mf01-floor-plan-http-" + Guid.NewGuid().ToString("N"));
        using var factory = new FloorPlanApiFactory(new TestStore(floor), root);
        using var client = factory.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        using var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new("image/png");
        multipart.Add(file, "file", "floor.png");

        var response = await client.PostAsync($"/api/floors/{floor.FloorId}/map", multipart);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        if (expectedStatus == 200)
        {
            Assert.NotNull(floor.MapAssetUrl);
            var mapUri = new Uri(floor.MapAssetUrl);
            Assert.Equal($"/api/floors/{floor.FloorId}/map", mapUri.AbsolutePath);
            Assert.StartsWith("?v=", mapUri.Query, StringComparison.Ordinal);
            Assert.True(Directory.Exists(root));
            Assert.Single(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
        }
        else Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task UploadedMapCanBeRetrievedWithItsContentType()
    {
        var floor = new Floor
        {
            FloorId = Guid.NewGuid(),
            SupermarketId = Guid.NewGuid(),
            FloorNumber = 1,
            Name = "Ground floor"
        };
        var root = Path.Combine(Path.GetTempPath(), "mf01-floor-plan-http-" + Guid.NewGuid().ToString("N"));
        using var factory = new FloorPlanApiFactory(new TestStore(floor), root);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "ADMIN");
        using var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new("image/png");
        multipart.Add(file, "file", "floor.png");

        var upload = await client.PostAsync($"/api/floors/{floor.FloorId}/map", multipart);
        upload.EnsureSuccessStatusCode();
        var response = await client.GetAsync($"/api/floors/{floor.FloorId}/map");

        response.EnsureSuccessStatusCode();
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task UploadRejectsContentThatDoesNotMatchTheDeclaredFormat()
    {
        var floor = NewFloor();
        var root = TempRoot();
        using var factory = new FloorPlanApiFactory(new TestStore(floor), root);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "ADMIN");
        using var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent("not a png"u8.ToArray());
        file.Headers.ContentType = new("image/png");
        multipart.Add(file, "file", "floor.png");

        var response = await client.PostAsync($"/api/floors/{floor.FloorId}/map", multipart);

        Assert.Equal(422, (int)response.StatusCode);
        Assert.Null(floor.MapAssetUrl);
        Assert.Empty(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("empty", "image/png", "floor.png", 20971520)]
    [InlineData("unsupported", "text/plain", "floor.txt", 20971520)]
    [InlineData("oversized", "image/png", "floor.png", 8)]
    public async Task UploadRejectsEmptyUnsupportedAndOversizedFiles(string scenario, string contentType, string filename, long maxBytes)
    {
        var floor = NewFloor();
        var root = TempRoot();
        using var factory = new FloorPlanApiFactory(new TestStore(floor), root, maxBytes);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "ADMIN");
        var bytes = scenario switch
        {
            "empty" => [],
            "unsupported" => "plain text"u8.ToArray(),
            _ => Png
        };
        using var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new(contentType);
        multipart.Add(file, "file", filename);

        var response = await client.PostAsync($"/api/floors/{floor.FloorId}/map", multipart);

        Assert.Equal(422, (int)response.StatusCode);
        Assert.Null(floor.MapAssetUrl);
        if (Directory.Exists(root)) Assert.Empty(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ReplacingMapRemovesTheSupersededStoredFile()
    {
        var floor = NewFloor();
        var root = TempRoot();
        using var factory = new FloorPlanApiFactory(new TestStore(floor), root);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "ADMIN");

        await UploadPng(client, floor.FloorId);
        var firstUrl = floor.MapAssetUrl;
        await UploadPng(client, floor.FloorId);

        Assert.NotEqual(firstUrl, floor.MapAssetUrl);
        Assert.Single(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
        var response = await client.GetAsync($"/api/floors/{floor.FloorId}/map");
        response.EnsureSuccessStatusCode();
        Assert.Equal(Png, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task FailedPersistenceKeepsThePreviousStoredFile()
    {
        var floor = NewFloor();
        var store = new TestStore(floor);
        var root = TempRoot();
        using var factory = new FloorPlanApiFactory(store, root);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "ADMIN");
        await UploadPng(client, floor.FloorId);
        var previous = Assert.Single(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
        store.FailUpdates = true;

        using var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new("image/png");
        multipart.Add(file, "file", "replacement.png");
        var response = await client.PostAsync($"/api/floors/{floor.FloorId}/map", multipart);

        Assert.Equal(500, (int)response.StatusCode);
        Assert.Equal(previous, Assert.Single(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)));
        var restored = await client.GetAsync($"/api/floors/{floor.FloorId}/map");
        restored.EnsureSuccessStatusCode();
        Assert.Equal(Png, await restored.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ConcurrentReplacementsLeaveOneReadableCurrentFile()
    {
        var floor = NewFloor();
        var root = TempRoot();
        using var factory = new FloorPlanApiFactory(new TestStore(floor), root);
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        firstClient.DefaultRequestHeaders.Add("X-Test-Role", "ADMIN");
        secondClient.DefaultRequestHeaders.Add("X-Test-Role", "ADMIN");

        await Task.WhenAll(UploadPng(firstClient, floor.FloorId), UploadPng(secondClient, floor.FloorId));

        Assert.Single(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
        var response = await firstClient.GetAsync($"/api/floors/{floor.FloorId}/map");
        response.EnsureSuccessStatusCode();
        Assert.Equal(Png, await response.Content.ReadAsByteArrayAsync());
    }

    private static Floor NewFloor() => new()
    {
        FloorId = Guid.NewGuid(),
        SupermarketId = Guid.NewGuid(),
        FloorNumber = 1,
        Name = "Ground floor"
    };

    private static string TempRoot() => Path.Combine(Path.GetTempPath(), "mf01-floor-plan-http-" + Guid.NewGuid().ToString("N"));

    private static async Task UploadPng(HttpClient client, Guid floorId)
    {
        using var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new("image/png");
        multipart.Add(file, "file", "floor.png");
        (await client.PostAsync($"/api/floors/{floorId}/map", multipart)).EnsureSuccessStatusCode();
    }

    private sealed class FloorPlanApiFactory(TestStore store, string root, long maxBytes = 20L * 1024 * 1024) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing").ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bootstrap:Enabled"] = "false",
                ["CameraHealth:Enabled"] = "false",
                ["FloorPlan:Provider"] = "Local",
                ["FloorPlan:Root"] = root,
                ["FloorPlan:MaxBytes"] = maxBytes.ToString()
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISetupStore>();
                services.AddSingleton<ISetupStore>(store);
                services.PostConfigure<Supermarket.Api.JwtOptions>(o => o.Key = new string('x', 48));
                services.PostConfigure<Supermarket.Infrastructure.HealthWorkerOptions>(o => o.Enabled = false);
                services.AddAuthentication(o =>
                    {
                        o.DefaultAuthenticateScheme = "FloorPlanTest";
                        o.DefaultChallengeScheme = "FloorPlanTest";
                    })
                    .AddScheme<AuthenticationSchemeOptions, FloorPlanTestAuth>("FloorPlanTest", _ => { });
            });
        }
    }

    private sealed class FloorPlanTestAuth(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            if (role.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role)],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class TestStore(Floor floor) : ISetupStore
    {
        private readonly List<Entity> values = [floor];
        public bool FailUpdates { get; set; }

        public Task<T?> Find<T>(Guid id, CancellationToken ct = default) where T : Entity, new()
        {
            var key = typeof(T).GetProperties().First(property => property.Name.EndsWith("Id", StringComparison.Ordinal));
            return Task.FromResult(values.OfType<T>().FirstOrDefault(value => Equals(key.GetValue(value), id)));
        }

        public Task<List<T>> List<T>(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) where T : Entity, new() =>
            Task.FromResult((filter is null ? values.OfType<T>() : values.OfType<T>().Where(filter.Compile())).ToList());

        public Task Add<T>(T entity, CancellationToken ct = default) where T : Entity, new()
        {
            values.Add(entity);
            return Task.CompletedTask;
        }

        public Task Update<T>(T entity, CancellationToken ct = default) where T : Entity, new() => FailUpdates
            ? throw new Supermarket.Application.ApplicationException("SAVE_FAILED", "The floor could not be saved.", 500)
            : Task.CompletedTask;
        public Task Remove<T>(T entity, CancellationToken ct = default) where T : Entity, new() => Task.CompletedTask;
        public Task<TResult> Transaction<TResult>(Func<Task<TResult>> work, CancellationToken ct = default) => work();
    }
}
