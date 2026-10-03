using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Supermarket.Application;
using Supermarket.Domain;
using Store = Supermarket.Domain.Supermarket;
using AppError = Supermarket.Application.ApplicationException;
using Xunit;
namespace Supermarket.Tests;

public sealed class ConfigurationManagementTests
{
    private static (MonitoringSetupTests.Harness H, Floor Floor, Store Store) World()
    {
        var h = new MonitoringSetupTests.Harness(); h.AddRule();
        var store = new Store { SupermarketId = Guid.NewGuid(), Name = "Default store" };
        var floor = new Floor { FloorId = h.Zone.FloorId, SupermarketId = store.SupermarketId, FloorNumber = 1, Name = "Ground floor", MapAssetUrl = "http://api/floors/map?v=current", MapWidth = 1200, MapHeight = 800 };
        h.Store.Values.AddRange([store, floor]);
        return (h, floor, store);
    }

    [Theory, InlineData("DRAFT"), InlineData("INACTIVE")]
    public async Task DeleteRemovesOnlyTheSelectedConfigurationAndRules(string status)
    {
        var (h, floor, _) = World(); h.Configuration.Status = status;
        var other = new MonitoringConfiguration { ConfigId = Guid.NewGuid(), ZoneId = Guid.NewGuid(), Name = "Other" };
        var otherRule = new MonitoringRule { RuleId = Guid.NewGuid(), ConfigId = other.ConfigId };
        h.Store.Values.AddRange([other, otherRule]);
        var spatial = JsonSerializer.Serialize(h.Store.Values.Where(e => e is Floor or Zone or Camera or CameraConnection or CameraZoneMapping));
        await h.Setup.Delete(h.Zone.ZoneId, new(h.Configuration.ConfigId, h.Configuration.UpdatedAt), default);
        Assert.DoesNotContain(h.Configuration, h.Store.Values);
        Assert.DoesNotContain(h.Store.Values.OfType<MonitoringRule>(), r => r.ConfigId == h.Configuration.ConfigId);
        Assert.Contains(other, h.Store.Values); Assert.Contains(otherRule, h.Store.Values);
        Assert.Equal(spatial, JsonSerializer.Serialize(h.Store.Values.Where(e => e is Floor or Zone or Camera or CameraConnection or CameraZoneMapping)));
        var overview = await new SetupOverview(h.Store, new MonitoringSetupTests.User("ADMIN"), new Clock()).Get(default);
        Assert.Null(overview.Floors.Single(f => f.FloorId == floor.FloorId).Zones.Single().Configuration);
    }

    [Theory, InlineData("active", "MONITORING_ACTIVE"), InlineData("stale", "CONFIGURATION_CHANGED"), InlineData("replacement", "CONFIGURATION_CHANGED")]
    public async Task DeleteRejectsActiveOrChangedConfigurationWithoutRemovingAnything(string state, string code)
    {
        var (h, _, _) = World();
        if (state == "active") h.Configuration.Status = "ACTIVE";
        var request = new MonitoringDeleteRequest(state == "replacement" ? Guid.NewGuid() : h.Configuration.ConfigId,
            state == "stale" ? DateTime.UnixEpoch : h.Configuration.UpdatedAt);
        var before = JsonSerializer.Serialize(h.Store.Values);
        Assert.Equal(code, (await Assert.ThrowsAsync<AppError>(() => h.Setup.Delete(h.Zone.ZoneId, request, default))).Code);
        Assert.Equal(before, JsonSerializer.Serialize(h.Store.Values));
    }

    [Fact]
    public async Task DeleteDetachesRuleReferencesButRetainsHistoricalEvidence()
    {
        var (h, _, _) = World();
        var rule = h.Store.Values.OfType<MonitoringRule>().Single();
        var evidence = new OperationalEvent { EventId = Guid.NewGuid(), CameraId = h.Camera.CameraId,
            ZoneId = h.Zone.ZoneId, RuleId = rule.RuleId, EventType = "QUEUE_LENGTH", MetricValue = 5,
            MetadataJson = "{\"warningThreshold\":3,\"configurationVersion\":\"old\"}" };
        h.Store.Values.Add(evidence);
        await h.Setup.Delete(h.Zone.ZoneId, new(h.Configuration.ConfigId, h.Configuration.UpdatedAt), default);
        var retained = Assert.Single(h.Store.Values.OfType<OperationalEvent>());
        Assert.Null(retained.RuleId);
        Assert.Equal(evidence.EventId, retained.EventId);
        Assert.Equal(h.Zone.ZoneId, retained.ZoneId);
        Assert.Equal(5, retained.MetricValue);
        Assert.Equal("{\"warningThreshold\":3,\"configurationVersion\":\"old\"}", retained.MetadataJson);
    }

    [Fact]
    public async Task DeletedZoneCanHaveANewDraftAndOldDeleteCannotRemoveTheReplacement()
    {
        var (h, _, _) = World();
        var oldRequest = new MonitoringDeleteRequest(h.Configuration.ConfigId, h.Configuration.UpdatedAt);
        var type = h.Store.Values.OfType<IncidentType>().Single();
        await h.Setup.Delete(h.Zone.ZoneId, oldRequest, default);
        var saved = await h.Setup.Save(h.Zone.ZoneId, new("Replacement", Rules: [new(type.IncidentTypeId, 3, 5, "PEOPLE")]), default);
        Assert.NotEqual(oldRequest.ConfigId, saved.ConfigId);
        Assert.Equal("CONFIGURATION_CHANGED", (await Assert.ThrowsAsync<AppError>(() => h.Setup.Delete(h.Zone.ZoneId, oldRequest, default))).Code);
        Assert.Equal(saved.ConfigId, (await h.Setup.Get(h.Zone.ZoneId, default)).ConfigId);
    }

    [Fact]
    public async Task FloorDetailsRetainTheCurrentMapZonesAndCameras()
    {
        var (h, floor, store) = World();
        // The map changed after the editor opened. The details request carries no old asset metadata.
        floor.MapAssetUrl = "http://api/floors/map?v=new-upload"; floor.MapWidth = 1600; floor.MapHeight = 900;
        var spatial = JsonSerializer.Serialize(h.Store.Values.Where(e => e is Zone or Camera or CameraZoneMapping));
        var setup = new StoreSetup(h.Store, new MonitoringSetupTests.User("ADMIN"));
        var saved = await setup.UpdateFloorDetails(floor.FloorId, new(2, "  Upper floor  "), default);
        Assert.Equal("Upper floor", saved.Name); Assert.Equal(2, saved.FloorNumber);
        Assert.Equal(store.SupermarketId, saved.SupermarketId); Assert.Equal(floor.FloorId, saved.FloorId);
        Assert.Equal("http://api/floors/map?v=new-upload", saved.MapAssetUrl);
        Assert.Equal(1600, saved.MapWidth); Assert.Equal(900, saved.MapHeight);
        Assert.Equal(spatial, JsonSerializer.Serialize(h.Store.Values.Where(e => e is Zone or Camera or CameraZoneMapping)));
    }

    [Fact]
    public async Task DuplicateFloorNumberAndInvalidNameDoNotChangeTheFloor()
    {
        var (h, floor, store) = World();
        h.Store.Values.Add(new Floor { FloorId = Guid.NewGuid(), SupermarketId = store.SupermarketId, FloorNumber = 2 });
        var before = JsonSerializer.Serialize(floor);
        var setup = new StoreSetup(h.Store, new MonitoringSetupTests.User("ADMIN"));
        Assert.Equal("DUPLICATE", (await Assert.ThrowsAsync<AppError>(() => setup.UpdateFloorDetails(floor.FloorId, new(2, "Changed"), default))).Code);
        await Assert.ThrowsAsync<DomainException>(() => setup.UpdateFloorDetails(floor.FloorId, new(1, " "), default));
        Assert.Equal(before, JsonSerializer.Serialize(floor));
    }

    [Theory, InlineData("OPERATOR"), InlineData("MANAGER"), InlineData("STAFF")]
    public async Task ApplicationRejectsOtherRoles(string role)
    {
        var (h, floor, _) = World(); var current = new MonitoringSetupTests.User(role);
        await Assert.ThrowsAsync<AppError>(() => new MonitoringSetup(h.Store, current).Delete(h.Zone.ZoneId, new(h.Configuration.ConfigId, h.Configuration.UpdatedAt), default));
        await Assert.ThrowsAsync<AppError>(() => new StoreSetup(h.Store, current).UpdateFloorDetails(floor.FloorId, new(2, "Changed"), default));
    }

    [Theory, InlineData(null, 401), InlineData("OPERATOR", 403), InlineData("MANAGER", 403), InlineData("STAFF", 403), InlineData("ADMIN", 204)]
    public async Task DeleteEndpointRequiresAdminAndReturnsNoContent(string? role, int expected)
    {
        var (h, _, _) = World(); await using var factory = new SetupOverviewTests.Factory(h.Store);
        using var client = factory.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/zones/{h.Zone.ZoneId}/monitoring")
        { Content = JsonContent.Create(new MonitoringDeleteRequest(h.Configuration.ConfigId, h.Configuration.UpdatedAt)) };
        var response = await client.SendAsync(request);
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        Assert.Equal(expected != 204, h.Store.Values.Contains(h.Configuration));
        if (expected == 204) Assert.Equal("", await response.Content.ReadAsStringAsync());
    }

    [Theory, InlineData(null, 401), InlineData("OPERATOR", 403), InlineData("MANAGER", 403), InlineData("STAFF", 403), InlineData("ADMIN", 200)]
    public async Task FloorDetailsEndpointRequiresAdminAndRetainsMap(string? role, int expected)
    {
        var (h, floor, _) = World(); await using var factory = new SetupOverviewTests.Factory(h.Store);
        using var client = factory.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        var response = await client.PatchAsJsonAsync($"/api/floors/{floor.FloorId}/details", new FloorDetailsRequest(0, "Lobby"));
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        Assert.Equal(expected == 200 ? "Lobby" : "Ground floor", floor.Name);
        Assert.Equal("http://api/floors/map?v=current", floor.MapAssetUrl);
    }

    private sealed class Clock : IClock { public DateTime UtcNow => DateTime.UtcNow; }
}
