using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Supermarket.Api;
using Supermarket.Api.Controllers;
using Supermarket.Application;
using Supermarket.Domain;
using AppError = Supermarket.Application.ApplicationException;
using Xunit;

namespace Supermarket.Tests;

public sealed class ZoneAdjacencyTests
{
    private static (MonitoringSetupTests.MemoryStore Store, Zone Source, Zone SameFloor, Zone OtherFloor) World()
    {
        var source = new Zone { ZoneId = Guid.NewGuid(), FloorId = Guid.NewGuid(), Code = "QUEUE", Name = "Queue" };
        var sameFloor = new Zone { ZoneId = Guid.NewGuid(), FloorId = source.FloorId, Code = "CHECKOUT", Name = "Checkout" };
        var otherFloor = new Zone { ZoneId = Guid.NewGuid(), FloorId = Guid.NewGuid(), Code = "STORAGE", Name = "Storage" };
        var store = new MonitoringSetupTests.MemoryStore();
        store.Values.AddRange([source, sameFloor, otherFloor]);
        return (store, source, sameFloor, otherFloor);
    }

    [Fact]
    public async Task ReplaceStoresBothDirectionsAndListsAdjacentZoneDetails()
    {
        var (store, source, sameFloor, _) = World();
        var service = new ZoneAdjacencies(store, new MonitoringSetupTests.User("ADMIN"));

        var result = await service.Replace(source.ZoneId, new([sameFloor.ZoneId]), default);

        var links = store.Values.OfType<ZoneAdjacency>().ToArray();
        Assert.Equal(2, links.Length);
        Assert.Contains(links, x => x.ZoneId == source.ZoneId && x.AdjacentZoneId == sameFloor.ZoneId);
        Assert.Contains(links, x => x.ZoneId == sameFloor.ZoneId && x.AdjacentZoneId == source.ZoneId);
        var adjacent = Assert.Single(result);
        Assert.Equal(sameFloor.ZoneId, adjacent.ZoneId);
        Assert.Equal("CHECKOUT", adjacent.Code);
        Assert.Equal("Checkout", adjacent.Name);
    }

    [Fact]
    public async Task ReplaceRemovesBothDirectionsButPreservesUnrelatedRelationships()
    {
        var (store, source, sameFloor, _) = World();
        var another = new Zone { ZoneId = Guid.NewGuid(), FloorId = source.FloorId, Code = "ENTRY", Name = "Entry" };
        store.Values.Add(another);
        store.Values.AddRange([
            Link(source, sameFloor), Link(sameFloor, source),
            Link(sameFloor, another), Link(another, sameFloor)
        ]);
        var service = new ZoneAdjacencies(store, new MonitoringSetupTests.User("ADMIN"));

        await service.Replace(source.ZoneId, new([another.ZoneId]), default);

        var links = store.Values.OfType<ZoneAdjacency>().ToArray();
        Assert.DoesNotContain(links, x => x.ZoneId == source.ZoneId && x.AdjacentZoneId == sameFloor.ZoneId);
        Assert.DoesNotContain(links, x => x.ZoneId == sameFloor.ZoneId && x.AdjacentZoneId == source.ZoneId);
        Assert.Contains(links, x => x.ZoneId == source.ZoneId && x.AdjacentZoneId == another.ZoneId);
        Assert.Contains(links, x => x.ZoneId == another.ZoneId && x.AdjacentZoneId == source.ZoneId);
        Assert.Contains(links, x => x.ZoneId == sameFloor.ZoneId && x.AdjacentZoneId == another.ZoneId);
        Assert.Contains(links, x => x.ZoneId == another.ZoneId && x.AdjacentZoneId == sameFloor.ZoneId);
    }

    [Fact]
    public async Task ReplaceRejectsSelfDuplicateMissingAndCrossFloorTargetsWithoutMutation()
    {
        var (store, source, sameFloor, otherFloor) = World();
        var service = new ZoneAdjacencies(store, new MonitoringSetupTests.User("ADMIN"));
        var before = store.Values.Count;

        Assert.Equal("ZONE_ADJACENCY_SELF", (await Assert.ThrowsAsync<AppError>(() => service.Replace(source.ZoneId, new([source.ZoneId]), default))).Code);
        Assert.Equal("ZONE_ADJACENCY_DUPLICATE", (await Assert.ThrowsAsync<AppError>(() => service.Replace(source.ZoneId, new([sameFloor.ZoneId, sameFloor.ZoneId]), default))).Code);
        Assert.Equal("NOT_FOUND", (await Assert.ThrowsAsync<AppError>(() => service.Replace(source.ZoneId, new([Guid.NewGuid()]), default))).Code);
        Assert.Equal("ZONE_ADJACENCY_CROSS_FLOOR", (await Assert.ThrowsAsync<AppError>(() => service.Replace(source.ZoneId, new([otherFloor.ZoneId]), default))).Code);
        Assert.Equal(before, store.Values.Count);
        Assert.Empty(store.Values.OfType<ZoneAdjacency>());
    }

    [Theory]
    [InlineData("OPERATOR")]
    [InlineData("MANAGER")]
    [InlineData("STAFF")]
    public async Task ReplaceRequiresAdmin(string role)
    {
        var (store, source, sameFloor, _) = World();
        var service = new ZoneAdjacencies(store, new MonitoringSetupTests.User(role));

        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<AppError>(() => service.Replace(source.ZoneId, new([sameFloor.ZoneId]), default))).Code);
        Assert.Empty(store.Values.OfType<ZoneAdjacency>());
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("OPERATOR")]
    [InlineData("MANAGER")]
    public async Task OperationalViewersCanListAdjacency(string role)
    {
        var (store, source, sameFloor, _) = World();
        store.Values.Add(Link(source, sameFloor));

        var result = await new ZoneAdjacencies(store, new MonitoringSetupTests.User(role)).List(source.ZoneId, default);

        Assert.Equal(sameFloor.ZoneId, Assert.Single(result).ZoneId);
    }

    [Fact]
    public void ControllerDeclaresViewerReadAndAdminWriteRoutes()
    {
        var controller = typeof(ZoneAdjacenciesController);
        Assert.Equal("api/zones/{zoneId:guid}/adjacent-zones", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        var methods = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var get = methods.Single(x => x.GetCustomAttribute<HttpGetAttribute>() is not null);
        var put = methods.Single(x => x.GetCustomAttribute<HttpPutAttribute>() is not null);
        Assert.Equal(AccessRoles.Viewer, get.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.Equal(AccessRoles.Admin, put.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
    }

    [Fact]
    public async Task SwaggerPublishesZoneAdjacencyPathAndSchemas()
    {
        var (store, _, _, _) = World();
        await using var root = new SetupOverviewTests.Factory(store);
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Swagger:Enabled"] = "true" })));
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var jsonRoot = document.RootElement;
        var path = jsonRoot.GetProperty("paths").GetProperty("/api/zones/{zoneId}/adjacent-zones");
        Assert.True(path.TryGetProperty("get", out _));
        Assert.True(path.TryGetProperty("put", out _));
        var schemas = jsonRoot.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("AdjacentZoneView", out _));
        Assert.True(schemas.TryGetProperty("ZoneAdjacencyReplaceRequest", out _));
    }

    private static ZoneAdjacency Link(Zone from, Zone to) => new()
    {
        ZoneAdjacencyId = Guid.NewGuid(),
        ZoneId = from.ZoneId,
        AdjacentZoneId = to.ZoneId,
        CreatedAt = DateTime.UtcNow
    };
}
