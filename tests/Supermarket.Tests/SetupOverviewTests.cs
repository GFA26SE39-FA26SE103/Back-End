using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
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
using Store = Supermarket.Domain.Supermarket;
using AppError = Supermarket.Application.ApplicationException;
using Xunit;
namespace Supermarket.Tests;

public sealed class SetupOverviewTests
{
    private sealed class Clock : IClock { public DateTime UtcNow => new(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc); }
    private static SetupOverview Overview(MonitoringSetupTests.MemoryStore store, string role = "ADMIN") => new(store, new MonitoringSetupTests.User(role), new Clock(), new CameraHealthRuntimeState());
    private static MonitoringSetupTests.Harness World()
    {
        var h = new MonitoringSetupTests.Harness();
        var supermarket = new Store { SupermarketId = Guid.NewGuid(), Name = "Default store" };
        h.Store.Values.Add(supermarket);
        h.Store.Values.Add(new Floor { FloorId = h.Zone.FloorId, SupermarketId = supermarket.SupermarketId, FloorNumber = 1, Name = "Ground floor", MapAssetUrl = "/api/floors/map" });
        h.AddRule();
        return h;
    }

    [Fact]
    public async Task EmptyStoreDoesNotClaimSetupOrMonitoringComplete()
    {
        var data = await Overview(new()).Get(default);
        Assert.False(data.HasDefaultStore);
        Assert.Equal(0, data.Totals.ActiveConfigurationCount);
        Assert.Empty(data.Floors);
        Assert.All(data.Steps, s => { Assert.Equal(0, s.Completed); Assert.Equal(0, s.Total); });
    }

    [Fact]
    public async Task MissingConfigurationAndIncompleteFloorAreReportedWithoutCreatingRecords()
    {
        var h = World();
        h.Store.Values.Remove(h.Configuration);
        h.Store.Values.OfType<Floor>().Single().MapAssetUrl = null;
        var before = JsonSerializer.Serialize(h.Store.Values);
        var data = await Overview(h.Store).Get(default);
        var zone = Assert.Single(Assert.Single(data.Floors).Zones);
        Assert.Null(zone.Configuration);
        Assert.False(zone.CanActivate);
        Assert.Contains(zone.Issues, i => i.Code == "CONFIGURATION_MISSING");
        Assert.Equal(0, data.Steps.Single(s => s.Code == "floor-zones").Completed);
        Assert.Equal(before, JsonSerializer.Serialize(h.Store.Values));
    }

    [Theory]
    [InlineData("ready")]
    [InlineData("no-rules")]
    [InlineData("disabled")]
    [InlineData("roi")]
    [InlineData("density")]
    [InlineData("cross-floor")]
    [InlineData("demo")]
    public async Task DashboardReadinessMatchesReviewAndDoesNotChangeSavedData(string state)
    {
        var h = World();
        switch (state)
        {
            case "no-rules": h.Store.Values.RemoveAll(e => e is MonitoringRule); break;
            case "disabled": h.Connection.IsEnabled = false; break;
            case "roi": h.Store.Values.OfType<CameraZoneMapping>().Single().RoiPolygon = "[]"; break;
            case "density": h.Store.Values.RemoveAll(e => e is MonitoringRule or IncidentType); h.AddRule("OVERCROWDING_CONGESTION", "CROWD_DENSITY", "PEOPLE_PER_M2"); h.Zone.AreaM2 = null; break;
            case "cross-floor":
                var store = h.Store.Values.OfType<Store>().Single();
                var floor = new Floor { FloorId = Guid.NewGuid(), SupermarketId = store.SupermarketId, FloorNumber = 2 };
                h.Store.Values.Add(floor); h.Camera.FloorId = floor.FloorId; break;
            case "demo": h.Connection.SourceType = "DEMO"; h.Connection.StreamUri = "demo://camera/main"; break;
        }
        var before = JsonSerializer.Serialize(h.Store.Values);
        var review = await h.Setup.Review(h.Zone.ZoneId, default);
        var data = await Overview(h.Store).Get(default);
        var zone = data.Floors.SelectMany(f => f.Zones).Single();
        Assert.Equal(review.CanActivate, zone.SetupReady);
        Assert.Equal(review.CanActivate, zone.CanActivate);
        Assert.Equal(review.Issues.Select(i => i.Code), zone.Issues.Select(i => i.Code));
        Assert.Equal(before, JsonSerializer.Serialize(h.Store.Values));
        if (state is "roi" or "cross-floor") Assert.Equal(0, data.Steps.Single(s => s.Code == "mapping-roi").Completed);
    }

    [Fact]
    public async Task ActiveConfigurationAndOfflineCameraRemainSeparateAndSecretsAreOmitted()
    {
        var h = World();
        h.Configuration.Status = "ACTIVE"; h.Camera.HealthStatus = "OFFLINE";
        h.Connection.StreamUri = "http://sensitive-stream.example/video";
        h.Connection.Username = "sensitive-user"; h.Connection.CredentialSecretRef = "sensitive-secret";
        h.Store.Values.Add(new CameraHealthEvent { HealthEventId = Guid.NewGuid(), CameraId = h.Camera.CameraId, EventType = "STREAM_UNAVAILABLE", Status = "INVESTIGATING" });
        h.Store.Values.Add(new CameraHealthEvent { HealthEventId = Guid.NewGuid(), CameraId = h.Camera.CameraId, Status = "RESOLVED" });
        var data = await Overview(h.Store).Get(default);
        Assert.Equal(1, data.Totals.ActiveConfigurationCount);
        Assert.Equal(0, data.Totals.ReadyToActivateCount);
        Assert.Equal(0, data.Totals.OnlineCameraCount);
        Assert.Equal(1, data.Totals.EnabledCameraCount);
        Assert.Equal("INVESTIGATING", Assert.Single(data.HealthEvents).Status);
        Assert.True(data.Floors.Single().Zones.Single().SetupReady);
        Assert.DoesNotContain("sensitive", JsonSerializer.Serialize(data));
        h.Camera.HealthStatus = "ONLINE"; h.Connection.IsEnabled = false;
        Assert.Equal(0, (await Overview(h.Store).Get(default)).Totals.OnlineCameraCount);
    }

    [Fact]
    public async Task SeveralMappingsBlockAmbiguousMeasurementWithoutActivatingUnconfiguredZones()
    {
        var h = World();
        var second = new Camera { CameraId = Guid.NewGuid(), FloorId = h.Zone.FloorId, Status = "ACTIVE", Code = "SECOND" };
        var zone = new Zone { ZoneId = Guid.NewGuid(), FloorId = h.Zone.FloorId, Code = "SECOND-ZONE" };
        var roi = h.Store.Values.OfType<CameraZoneMapping>().Single().RoiPolygon;
        h.Store.Values.AddRange([second, zone,
            new CameraZoneMapping { CameraZoneId = Guid.NewGuid(), CameraId = second.CameraId, ZoneId = h.Zone.ZoneId, RoiPolygon = roi },
            new CameraZoneMapping { CameraZoneId = Guid.NewGuid(), CameraId = h.Camera.CameraId, ZoneId = zone.ZoneId, RoiPolygon = roi }]);
        var data = await Overview(h.Store).Get(default);
        var first = data.Floors.Single().Zones.Single(z => z.ZoneId == h.Zone.ZoneId);
        Assert.False(first.CanActivate);
        Assert.Equal(2, first.Cameras.Length);
        Assert.Contains(first.Issues, i => i.Code == "MEASUREMENT_SOURCE_AMBIGUOUS");
        Assert.Equal(0, data.Totals.ReadyToActivateCount);
        Assert.Equal(2, data.Steps.Single(s => s.Code == "mapping-roi").Completed);
    }

    [Theory, InlineData("OPERATOR"), InlineData("MANAGER"), InlineData("STAFF")]
    public async Task ApplicationRejectsOtherRoles(string role)
    {
        var error = await Assert.ThrowsAsync<AppError>(() => Overview(new(), role).Get(default));
        Assert.Equal(403, error.Status);
    }

    [Theory, InlineData(null, 401), InlineData("OPERATOR", 403), InlineData("MANAGER", 403), InlineData("STAFF", 403), InlineData("ADMIN", 200)]
    public async Task EndpointEnforcesAdminAndReturnsUncachedSnapshot(string? role, int expected)
    {
        await using var factory = new Factory(World().Store);
        using var client = factory.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        var response = await client.GetAsync("/api/setup/overview");
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        if (expected == 200)
        {
            Assert.True(response.Headers.CacheControl?.NoStore);
            var data = await response.Content.ReadFromJsonAsync<SetupOverviewView>();
            Assert.Equal(1, data!.Totals.ZoneCount);
            Assert.Equal(DateTimeKind.Utc, data.GeneratedAt.Kind);
        }
    }

    internal sealed class Factory(MonitoringSetupTests.MemoryStore store) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing").ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            { ["Bootstrap:Enabled"] = "false", ["CameraHealth:Enabled"] = "false" }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISetupStore>(); services.AddSingleton<ISetupStore>(store);
                // This API harness uses an in-memory store, not the SQL runtime repository.
                services.RemoveAll<IMonitoringIncidentQueries>();
                services.PostConfigure<Supermarket.Infrastructure.Monitoring.MonitoringWorkerOptions>(o => o.Enabled = false);
                services.PostConfigure<Supermarket.Api.JwtOptions>(o => o.Key = new string('x', 48));
                services.PostConfigure<Supermarket.Infrastructure.HealthWorkerOptions>(o => o.Enabled = false);
                services.AddAuthentication(o => { o.DefaultAuthenticateScheme = "SetupTest"; o.DefaultChallengeScheme = "SetupTest"; })
                    .AddScheme<AuthenticationSchemeOptions, Auth>("SetupTest", _ => { });
            });
        }
    }
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e) : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
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
