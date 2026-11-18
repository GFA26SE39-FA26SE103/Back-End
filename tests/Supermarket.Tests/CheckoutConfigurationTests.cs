using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Supermarket.Api;
using Supermarket.Api.Controllers;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Infrastructure.Persistence;
using Xunit;
using AppError = Supermarket.Application.ApplicationException;
using Db = Supermarket.Infrastructure.Persistence.Scaffolded;

namespace Supermarket.Tests;

public sealed class CheckoutConfigurationTests
{
    private static readonly Point[] Triangle = [new(0, 0), new(1, 0), new(0, 1)];
    private static readonly DateTime Version = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DomainPropertiesExactlyMatchTheGeneratedScalars()
    {
        Match(typeof(CheckoutCounter), typeof(Db.CheckoutCounter),
            "CheckoutCounterId", "ZoneId", "Code", "Name", "IsEnabled", "CreatedAt", "UpdatedAt");
        Match(typeof(CameraCheckoutMapping), typeof(Db.CameraCheckoutMapping),
            "MappingId", "CameraId", "CheckoutCounterId", "LaneRoi", "StaffRoi", "ServiceRoi", "IsEnabled", "IsQueueSource", "CreatedAt", "UpdatedAt");
        static void Match(Type domain, Type generated, params string[] names)
        {
            Assert.Equal(names.Order(), domain.GetProperties().Select(p => p.Name).Order());
            foreach (var property in domain.GetProperties())
                Assert.Equal(generated.GetProperty(property.Name)!.PropertyType, property.PropertyType);
            Assert.True(typeof(TrackedEntity).IsAssignableFrom(domain));
        }
    }

    [Theory]
    [InlineData(0, 16, 128)]
    [InlineData(128, -1, 128)]
    [InlineData(128, 16, 0)]
    [InlineData(int.MaxValue, 16, 128)]
    public void InvalidOptionsFailBeforeUse(int counters, int mappings, int cameras)
    {
        var options = new CheckoutConfigurationOptions { MaxCountersPerZone = counters,
            MaxMappingsPerCounter = mappings, MaxMappingsPerCamera = cameras };
        Assert.False(options.IsValid());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Harness(options: options));
    }

    [Fact]
    public void DefaultCapacitiesAreExplicit()
    {
        var options = new CheckoutConfigurationOptions();
        Assert.True(options.IsValid());
        Assert.Equal(128, options.MaxCountersPerZone);
        Assert.Equal(16, options.MaxMappingsPerCounter);
        Assert.Equal(128, options.MaxMappingsPerCamera);
    }

    [Fact]
    public async Task SchemaMissingPrecedesAnyStoreOrCheckoutQueryForEveryEndpoint()
    {
        var h = new Harness(); h.Schema.Available = false; h.Store.FailOnAccess = true;
        var actions = new Func<Task>[]
        {
            () => h.Setup.CountersByZone(h.Zone.ZoneId, default),
            () => h.Setup.Get(h.Counter.CheckoutCounterId, default),
            () => h.Setup.MappingsByCamera(h.Camera.CameraId, default),
            () => h.Setup.MappingsByCounter(h.Counter.CheckoutCounterId, default),
            () => h.Setup.Create(h.Zone.ZoneId, new("NEW", "New"), default),
            () => h.Setup.Update(h.Counter.CheckoutCounterId, new("C1", "Updated", ExpectedUpdatedAt: Version), default),
            () => h.Setup.Delete(h.Counter.CheckoutCounterId, Version, default),
            () => h.Setup.PutMapping(h.Counter.CheckoutCounterId, h.Camera.CameraId, new(Triangle, Triangle), default),
            () => h.Setup.DeleteMapping(h.Counter.CheckoutCounterId, h.Camera.CameraId, Version, default)
        };
        foreach (var action in actions)
        {
            var error = await Assert.ThrowsAsync<AppError>(action);
            Assert.Equal("CHECKOUT_SCHEMA_NOT_READY", error.Code); Assert.Equal(503, error.Status);
        }
        Assert.Equal(0, h.Queries.Calls);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("OPERATOR")]
    [InlineData("MANAGER")]
    public async Task ViewersCanReadAllScopedLists(string role)
    {
        var h = new Harness(role); h.AddMapping();
        Assert.Single(await h.Setup.CountersByZone(h.Zone.ZoneId, default));
        Assert.Equal(h.Counter.CheckoutCounterId, (await h.Setup.Get(h.Counter.CheckoutCounterId, default)).CheckoutCounterId);
        Assert.Single(await h.Setup.MappingsByCamera(h.Camera.CameraId, default));
        Assert.Single(await h.Setup.MappingsByCounter(h.Counter.CheckoutCounterId, default));
    }

    [Theory]
    [InlineData("STAFF")]
    [InlineData("UNKNOWN")]
    public async Task OtherRolesCannotRead(string role)
    {
        var h = new Harness(role);
        await Error("FORBIDDEN", () => h.Setup.CountersByZone(h.Zone.ZoneId, default));
        await Error("FORBIDDEN", () => h.Setup.Get(h.Counter.CheckoutCounterId, default));
        await Error("FORBIDDEN", () => h.Setup.MappingsByCamera(h.Camera.CameraId, default));
        await Error("FORBIDDEN", () => h.Setup.MappingsByCounter(h.Counter.CheckoutCounterId, default));
    }

    [Theory]
    [InlineData("OPERATOR")]
    [InlineData("MANAGER")]
    [InlineData("STAFF")]
    public async Task AllMutationsAreAdminOnly(string role)
    {
        var h = new Harness(role);
        await Error("FORBIDDEN", () => h.Setup.Create(h.Zone.ZoneId, new("NEW", "New"), default));
        await Error("FORBIDDEN", () => h.Setup.Update(h.Counter.CheckoutCounterId, new("C1", "New"), default));
        await Error("FORBIDDEN", () => h.Setup.Delete(h.Counter.CheckoutCounterId, Version, default));
        await Error("FORBIDDEN", () => h.Setup.PutMapping(h.Counter.CheckoutCounterId, h.Camera.CameraId, new(Triangle, Triangle), default));
        await Error("FORBIDDEN", () => h.Setup.DeleteMapping(h.Counter.CheckoutCounterId, h.Camera.CameraId, Version, default));
    }

    [Fact]
    public async Task CounterCrudPreservesIdentityZoneAndStoreOwnedTimestamps()
    {
        var h = new Harness();
        var created = await h.Setup.Create(h.Zone.ZoneId, new(" C2 ", " Second "), default);
        Assert.False(created.IsEnabled); Assert.Equal("C2", created.Code); Assert.Equal("Second", created.Name);
        Assert.Equal(h.Zone.ZoneId, created.ZoneId); Assert.Equal(Version, created.CreatedAt);
        var updated = await h.Setup.Update(created.CheckoutCounterId, new("C3", "Third", ExpectedUpdatedAt: created.UpdatedAt), default);
        Assert.Equal(created.CheckoutCounterId, updated.CheckoutCounterId); Assert.Equal(created.ZoneId, updated.ZoneId);
        Assert.Equal(created.CreatedAt, updated.CreatedAt); Assert.True(updated.UpdatedAt > created.UpdatedAt);
        await h.Setup.Delete(updated.CheckoutCounterId, updated.UpdatedAt, default);
        await Error("NOT_FOUND", () => h.Setup.Get(updated.CheckoutCounterId, default));
        Assert.Equal(3, h.Store.Transactions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("QUEUE")]
    [InlineData("checkout")]
    public async Task OnlyExactCheckoutZonesAcceptCounters(string? zoneType)
    {
        var h = new Harness(); h.Zone.ZoneType = zoneType;
        await h.Unchanged("CHECKOUT_ZONE_REQUIRED", () => h.Setup.Create(h.Zone.ZoneId, new("C2", "New"), default));
    }

    [Fact]
    public async Task CounterCodeIsUniqueWithinZoneAndCaseInsensitiveBeforeEdits()
    {
        var h = new Harness();
        await h.Unchanged("DUPLICATE", () => h.Setup.Create(h.Zone.ZoneId, new(" c1 ", "Duplicate"), default));
        var other = new CheckoutCounter { CheckoutCounterId = Guid.NewGuid(), ZoneId = h.Zone.ZoneId,
            Code = "OTHER", Name = "Other", UpdatedAt = Version };
        h.Store.Values.Add(other);
        await h.Unchanged("DUPLICATE", () => h.Setup.Update(other.CheckoutCounterId, new("c1", "Changed", ExpectedUpdatedAt: Version), default));
        h.Counter.ZoneId = Guid.NewGuid();
        Assert.Equal("C1", (await h.Setup.Create(h.Zone.ZoneId, new("C1", "Different zone"), default)).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingCounterRequiresMatchingVersionForUpdateAndDelete(bool stale)
    {
        var h = new Harness(); DateTime? expected = stale ? Version.AddSeconds(-1) : null;
        await h.Unchanged("CHECKOUT_CONFIGURATION_CHANGED", () => h.Setup.Update(h.Counter.CheckoutCounterId, new("C2", "Changed", ExpectedUpdatedAt: expected), default));
        await h.Unchanged("CHECKOUT_CONFIGURATION_CHANGED", () => h.Setup.Delete(h.Counter.CheckoutCounterId, expected, default));
    }

    [Fact]
    public async Task CreateRejectsAnUnexpectedVersion()
    {
        var h = new Harness();
        await h.Unchanged("CHECKOUT_CONFIGURATION_CHANGED", () => h.Setup.Create(h.Zone.ZoneId, new("C2", "New", ExpectedUpdatedAt: Version), default));
        await h.Unchanged("CHECKOUT_CONFIGURATION_CHANGED", () => h.Setup.PutMapping(h.Counter.CheckoutCounterId, h.Camera.CameraId,
            new(Triangle, Triangle, ExpectedUpdatedAt: Version), default));
    }

    [Fact]
    public async Task ActiveMonitoringRejectsEveryMutationWithoutChangingTheFixture()
    {
        var h = new Harness(); var mapping = h.AddMapping();
        h.Store.Values.Add(new MonitoringConfiguration { ConfigId = Guid.NewGuid(), ZoneId = h.Zone.ZoneId, Status = "ACTIVE" });
        await h.Unchanged("MONITORING_ACTIVE", () => h.Setup.Create(h.Zone.ZoneId, new("C2", "New"), default));
        await h.Unchanged("MONITORING_ACTIVE", () => h.Setup.Update(h.Counter.CheckoutCounterId, new("C2", "New", ExpectedUpdatedAt: Version), default));
        await h.Unchanged("MONITORING_ACTIVE", () => h.Setup.Delete(h.Counter.CheckoutCounterId, Version, default));
        await h.Unchanged("MONITORING_ACTIVE", () => h.Setup.PutMapping(h.Counter.CheckoutCounterId, h.Camera.CameraId,
            new(Triangle, Triangle, ExpectedUpdatedAt: mapping.UpdatedAt), default));
        await h.Unchanged("MONITORING_ACTIVE", () => h.Setup.DeleteMapping(h.Counter.CheckoutCounterId, h.Camera.CameraId, mapping.UpdatedAt, default));
    }

    [Theory]
    [InlineData("zone")]
    [InlineData("mapping")]
    [InlineData("source")]
    [InlineData("camera")]
    [InlineData("connection")]
    [InlineData("test")]
    [InlineData("result")]
    [InlineData("floor")]
    [InlineData("polygon")]
    public async Task EnablingCounterRejectsIncompleteConfigurationWithoutEdits(string reason)
    {
        var h = new Harness(); var mapping = h.AddMapping(enabled: true, source: true);
        var code = "CHECKOUT_CAMERA_NOT_READY";
        switch (reason)
        {
            case "zone": h.Zone.Status = "INACTIVE"; code = "CHECKOUT_ZONE_INACTIVE"; break;
            case "mapping": mapping.IsEnabled = false; mapping.IsQueueSource = false; code = "CHECKOUT_QUEUE_SOURCE_REQUIRED"; break;
            case "source": mapping.IsQueueSource = false; code = "CHECKOUT_QUEUE_SOURCE_REQUIRED"; break;
            case "camera": h.Camera.Status = "INACTIVE"; break;
            case "connection": h.Connection.IsEnabled = false; break;
            case "test": h.Connection.LastTestedAt = null; break;
            case "result": h.Connection.LastTestResult = "FAILED"; break;
            case "floor": h.Camera.FloorId = Guid.NewGuid(); code = "CROSS_FLOOR_MAPPING"; break;
            case "polygon": mapping.StaffRoi = "[]"; code = "INVALID_POLYGON"; break;
        }
        await h.Unchanged(code, () => h.Setup.Update(h.Counter.CheckoutCounterId, new("NEW", "Changed", true, Version), default));
    }

    [Fact]
    public async Task CounterEnableUsesConfigurationNotRuntimeHealthAndDisableDoesNotEditMappings()
    {
        var h = new Harness(); var mapping = h.AddMapping(enabled: true, source: true);
        h.Camera.HealthStatus = "OFFLINE"; h.Camera.LastSeenAt = null;
        var enabled = await h.Setup.Update(h.Counter.CheckoutCounterId, new("C1", "Counter", true, Version), default);
        Assert.True(enabled.IsEnabled);
        h.Connection.LastTestResult = "FAILED";
        var disabled = await h.Setup.Update(enabled.CheckoutCounterId, new("C1", "Counter", false, enabled.UpdatedAt), default);
        Assert.False(disabled.IsEnabled); Assert.True(mapping.IsEnabled); Assert.True(mapping.IsQueueSource);
        Assert.Empty(h.Store.Values.OfType<MonitoringConfiguration>());
    }

    [Fact]
    public async Task MappingCrudRoundTripsIndependentPolygonsAndPreservesIdentityAndTimestamps()
    {
        var h = new Harness(); Point[] staff = [new(.7m, .7m), new(1, .7m), new(.7m, 1)];
        var saved = await h.Setup.PutMapping(h.Counter.CheckoutCounterId, h.Camera.CameraId, new(Triangle, staff), default);
        Assert.Equal(Triangle, saved.LaneRoi); Assert.Equal(staff, saved.StaffRoi);
        Assert.False(saved.IsEnabled); Assert.False(saved.IsQueueSource);
        var row = Assert.Single(h.Store.Values.OfType<CameraCheckoutMapping>());
        Assert.Contains("\"x\"", row.LaneRoi); Assert.Contains("\"y\"", row.StaffRoi);
        var updated = await h.Setup.PutMapping(h.Counter.CheckoutCounterId, h.Camera.CameraId,
            new(staff, Triangle, true, true, saved.UpdatedAt, Triangle), default);
        Assert.Equal(saved.MappingId, updated.MappingId); Assert.Equal(saved.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt > saved.UpdatedAt);
        await h.Setup.DeleteMapping(h.Counter.CheckoutCounterId, h.Camera.CameraId, updated.UpdatedAt, default);
        Assert.Empty(h.Store.Values.OfType<CameraCheckoutMapping>());
    }

    [Theory]
    [InlineData("lane")]
    [InlineData("staff")]
    [InlineData("null")]
    [InlineData("too-many")]
    [InlineData("self-intersection")]
    public async Task BothPolygonsAreAlwaysValidatedEvenForDisabledMapping(string invalid)
    {
        var h = new Harness(); h.AddMapping();
        var bad = invalid == "too-many" ? Enumerable.Repeat(new Point(0, 0), 1001).ToArray()
            : invalid == "self-intersection" ? [new Point(0, 0), new Point(1, 1), new Point(0, 1), new Point(1, 0)] : Array.Empty<Point>();
        await h.Unchanged("INVALID_POLYGON", () => h.Setup.PutMapping(h.Counter.CheckoutCounterId, h.Camera.CameraId,
            new(invalid == "staff" ? Triangle : invalid == "null" ? null! : bad,
                invalid == "staff" ? bad : Triangle, ExpectedUpdatedAt: Version), default));
    }

    [Fact]
    public async Task MappingCannotCrossFloorsEvenWhenDisabled()
    {
        var h = new Harness(); h.Camera.FloorId = Guid.NewGuid();
        await h.Unchanged("CROSS_FLOOR_MAPPING", () => h.Setup.PutMapping(h.Counter.CheckoutCounterId, h.Camera.CameraId, new(Triangle, Triangle), default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingMappingRequiresMatchingVersionForUpdateAndDelete(bool stale)
    {
        var h = new Harness(); h.AddMapping(); DateTime? expected = stale ? Version.AddSeconds(-1) : null;
        await h.Unchanged("CHECKOUT_CONFIGURATION_CHANGED", () => h.Setup.PutMapping(h.Counter.CheckoutCounterId, h.Camera.CameraId,
            new(Triangle, Triangle, ExpectedUpdatedAt: expected), default));
        await h.Unchanged("CHECKOUT_CONFIGURATION_CHANGED", () => h.Setup.DeleteMapping(h.Counter.CheckoutCounterId, h.Camera.CameraId, expected, default));
    }

    [Fact]
    public async Task AtMostOneEnabledQueueSourceEvenWhileCounterIsDisabled()
    {
        var h = new Harness(); h.AddMapping(enabled: true, source: true);
        var second = h.AddCamera();
        await h.Unchanged("CHECKOUT_QUEUE_SOURCE_DUPLICATE", () => h.Setup.PutMapping(h.Counter.CheckoutCounterId, second.CameraId,
            new(Triangle, Triangle, true, true, ServiceRoi: Triangle), default));
        Assert.False(h.Counter.IsEnabled);
    }

    [Fact]
    public async Task EnabledQueueSourceRequiresServiceRoiBeforeSaving()
    {
        var h = new Harness();
        await h.Unchanged("CHECKOUT_SERVICE_ROI_REQUIRED", () => h.Setup.PutMapping(
            h.Counter.CheckoutCounterId, h.Camera.CameraId,
            new(Triangle, Triangle, true, true), default));
    }

    [Fact]
    public async Task EnabledCounterMustRetainExactlyOneSourceOnMappingEditsAndDeletes()
    {
        var h = new Harness(); h.Counter.IsEnabled = true; h.AddMapping(enabled: true, source: true);
        await h.Unchanged("CHECKOUT_QUEUE_SOURCE_REQUIRED", () => h.Setup.PutMapping(h.Counter.CheckoutCounterId, h.Camera.CameraId,
            new(Triangle, Triangle, true, false, Version), default));
        await h.Unchanged("CHECKOUT_QUEUE_SOURCE_REQUIRED", () => h.Setup.DeleteMapping(h.Counter.CheckoutCounterId, h.Camera.CameraId, Version, default));
        var second = h.AddCamera(); var extra = h.AddMapping(second, true, false);
        await h.Setup.DeleteMapping(h.Counter.CheckoutCounterId, second.CameraId, extra.UpdatedAt, default);
        Assert.Single(h.Store.Values.OfType<CameraCheckoutMapping>()); Assert.True(h.Counter.IsEnabled);
    }

    [Fact]
    public async Task DuplicateCameraCounterPairsAreRejectedBeforeEdits()
    {
        var h = new Harness(); h.AddMapping(); h.AddMapping();
        await h.Unchanged("CHECKOUT_MAPPING_DUPLICATE", () => h.Setup.PutMapping(h.Counter.CheckoutCounterId, h.Camera.CameraId,
            new(Triangle, Triangle, ExpectedUpdatedAt: Version), default));
    }

    [Fact]
    public async Task DeleteCounterRemovesOnlyItsDisabledMappingsInOneTransaction()
    {
        var h = new Harness(); h.AddMapping();
        var other = new CheckoutCounter { CheckoutCounterId = Guid.NewGuid(), ZoneId = h.Zone.ZoneId, Code = "OTHER" };
        var unrelated = new CameraCheckoutMapping { MappingId = Guid.NewGuid(), CheckoutCounterId = other.CheckoutCounterId };
        h.Store.Values.AddRange([other, unrelated]);
        await h.Setup.Delete(h.Counter.CheckoutCounterId, Version, default);
        Assert.DoesNotContain(h.Counter, h.Store.Values); Assert.Contains(other, h.Store.Values);
        Assert.Same(unrelated, Assert.Single(h.Store.Values.OfType<CameraCheckoutMapping>()));
        Assert.Equal(1, h.Store.Transactions);
        Assert.Equal(new[] { typeof(CameraCheckoutMapping), typeof(CheckoutCounter) }, h.Store.RemovedTypes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteRejectsAnEnabledCounterOrAnyEnabledMapping(bool enabledCounter)
    {
        var h = new Harness(); h.Counter.IsEnabled = enabledCounter; h.AddMapping(enabled: !enabledCounter);
        await h.Unchanged(enabledCounter ? "CHECKOUT_COUNTER_ENABLED" : "CHECKOUT_MAPPING_ENABLED",
            () => h.Setup.Delete(h.Counter.CheckoutCounterId, Version, default));
    }

    [Fact]
    public async Task ListsRejectOverflowRatherThanTruncatingAndPassConfiguredLimits()
    {
        var h = new Harness(options: new() { MaxCountersPerZone = 1, MaxMappingsPerCounter = 1, MaxMappingsPerCamera = 1 });
        h.Store.Values.Add(new CheckoutCounter { CheckoutCounterId = Guid.NewGuid(), ZoneId = h.Zone.ZoneId });
        h.AddMapping(); h.AddMapping();
        await Error("CHECKOUT_CAPACITY_EXCEEDED", () => h.Setup.CountersByZone(h.Zone.ZoneId, default));
        await Error("CHECKOUT_CAPACITY_EXCEEDED", () => h.Setup.MappingsByCounter(h.Counter.CheckoutCounterId, default));
        await Error("CHECKOUT_CAPACITY_EXCEEDED", () => h.Setup.MappingsByCamera(h.Camera.CameraId, default));
        Assert.All(h.Queries.Limits, value => Assert.Equal(1, value));
    }

    [Fact]
    public async Task CreateAtCounterCapacityAndMappingCapacityRejectWithoutEdits()
    {
        var h = new Harness(options: new() { MaxCountersPerZone = 1, MaxMappingsPerCounter = 1, MaxMappingsPerCamera = 1 });
        await h.Unchanged("CHECKOUT_CAPACITY_EXCEEDED", () => h.Setup.Create(h.Zone.ZoneId, new("C2", "Two"), default));
        h.AddMapping(); var second = h.AddCamera();
        await h.Unchanged("CHECKOUT_CAPACITY_EXCEEDED", () => h.Setup.PutMapping(h.Counter.CheckoutCounterId, second.CameraId, new(Triangle, Triangle), default));
        var other = new CheckoutCounter { CheckoutCounterId = Guid.NewGuid(), ZoneId = h.Zone.ZoneId, Code = "OTHER" };
        h.Store.Values.Add(other);
        await h.Unchanged("CHECKOUT_CAPACITY_EXCEEDED", () => h.Setup.PutMapping(other.CheckoutCounterId, h.Camera.CameraId, new(Triangle, Triangle), default));
    }

    [Theory]
    [InlineData("CountersByZone", "zone_id")]
    [InlineData("MappingsByCamera", "camera_id")]
    [InlineData("MappingsByCounter", "checkout_counter_id")]
    public void EfQueriesUseParameterizedScopeAndLimitPlusOneWithoutConnecting(string method, string column)
    {
        using var db = new Db.AppDbContext(new DbContextOptionsBuilder<Db.AppDbContext>()
            .UseSqlServer("Server=model-only;Database=model-only;Integrated Security=true;TrustServerCertificate=true").Options);
        var queries = new EfCheckoutConfigurationQueries(db);
        var query = (IQueryable)typeof(EfCheckoutConfigurationQueries).GetMethod(method + "Query", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(queries, [Guid.NewGuid(), 7])!;
        var sql = query.ToQueryString();
        Assert.Contains("TOP(@", sql); Assert.Contains("= 8", sql); Assert.Contains("WHERE", sql);
        Assert.Contains(column, sql); Assert.Contains("ORDER BY", sql);
        Assert.DoesNotContain("SELECT *", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FrozenRoutesAndControllerRolesAreExplicit()
    {
        var type = typeof(CheckoutCountersController);
        Assert.NotNull(type.GetCustomAttribute<ApiControllerAttribute>());
        Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>());
        var routes = type.GetMethods().SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>()
            .SelectMany(attribute => attribute.HttpMethods.Select(verb => (Verb: verb, Route: attribute.Template,
                Roles: method.GetCustomAttribute<AuthorizeAttribute>()?.Roles)))).ToArray();
        var expected = new[]
        {
            ("GET", "/api/zones/{zoneId:guid}/checkout-counters"),
            ("POST", "/api/zones/{zoneId:guid}/checkout-counters"),
            ("GET", "/api/checkout-counters/{counterId:guid}"),
            ("PATCH", "/api/checkout-counters/{counterId:guid}"),
            ("DELETE", "/api/checkout-counters/{counterId:guid}"),
            ("GET", "/api/cameras/{cameraId:guid}/checkout-mappings"),
            ("GET", "/api/checkout-counters/{counterId:guid}/camera-mappings"),
            ("PUT", "/api/checkout-counters/{counterId:guid}/camera-mappings/{cameraId:guid}"),
            ("DELETE", "/api/checkout-counters/{counterId:guid}/camera-mappings/{cameraId:guid}")
        };
        Assert.Equal(expected.Length, routes.Length);
        foreach (var (verb, route) in expected)
        {
            var actual = Assert.Single(routes, r => r.Verb == verb && r.Route == route);
            Assert.Equal(verb == "GET" ? AccessRoles.Viewer : AccessRoles.Admin, actual.Roles);
        }
    }

    private static async Task Error(string code, Func<Task> action)
        => Assert.Equal(code, (await Assert.ThrowsAsync<AppError>(action)).Code);

    private sealed class Harness
    {
        public MemoryStore Store { get; } = new();
        public Schema Schema { get; } = new();
        public Queries Queries { get; }
        public Zone Zone { get; } = new() { ZoneId = Guid.NewGuid(), FloorId = Guid.NewGuid(), ZoneType = "CHECKOUT", Status = "ACTIVE" };
        public CheckoutCounter Counter { get; }
        public Camera Camera { get; }
        public CameraConnection Connection { get; }
        public CheckoutConfiguration Setup { get; }
        public Harness(string role = "ADMIN", CheckoutConfigurationOptions? options = null)
        {
            Counter = new() { CheckoutCounterId = Guid.NewGuid(), ZoneId = Zone.ZoneId, Code = "C1", Name = "Counter", CreatedAt = Version, UpdatedAt = Version };
            Store.Values.AddRange([Zone, Counter]); Camera = AddCamera();
            Connection = Store.Values.OfType<CameraConnection>().Single(); Queries = new(Store);
            Setup = new(Store, new MonitoringSetupTests.User(role), Queries, Schema, options ?? new());
        }
        public Camera AddCamera()
        {
            var camera = new Camera { CameraId = Guid.NewGuid(), FloorId = Zone.FloorId, Status = "ACTIVE" };
            Store.Values.AddRange([camera, new CameraConnection { ConnectionId = Guid.NewGuid(), CameraId = camera.CameraId,
                IsEnabled = true, LastTestedAt = Version, LastTestResult = "SUCCESS", SourceType = "LIVE", Protocol = "HTTP", StreamUri = "http://camera/video" }]);
            return camera;
        }
        public CameraCheckoutMapping AddMapping(Camera? camera = null, bool enabled = false, bool source = false)
        {
            var row = new CameraCheckoutMapping { MappingId = Guid.NewGuid(), CameraId = (camera ?? Camera).CameraId,
                CheckoutCounterId = Counter.CheckoutCounterId, LaneRoi = JsonSerializer.Serialize(Triangle), StaffRoi = JsonSerializer.Serialize(Triangle),
                ServiceRoi = source ? JsonSerializer.Serialize(Triangle) : null,
                IsEnabled = enabled, IsQueueSource = source, CreatedAt = Version, UpdatedAt = Version };
            Store.Values.Add(row); return row;
        }
        public async Task Unchanged(string code, Func<Task> action)
        {
            var before = Snapshot(); await ErrorOrDomain(code, action);
            Assert.Equal(before, Snapshot()); Assert.Equal(0, Store.Writes);
        }
        private string Snapshot() => string.Join("\n", Store.Values.Select(e => JsonSerializer.Serialize(e, e.GetType())));
        private static async Task ErrorOrDomain(string code, Func<Task> action)
        {
            var error = await Record.ExceptionAsync(action);
            Assert.NotNull(error);
            Assert.Equal(code, error switch { AppError e => e.Code, DomainException e => e.Code, _ => error.GetType().Name });
        }
    }

    private sealed class Schema : ICheckoutSchema
    {
        public bool Available { get; set; } = true;
        public Task<bool> Ready(CancellationToken ct) => Task.FromResult(Available);
    }

    private sealed class Queries(MemoryStore store) : ICheckoutConfigurationQueries
    {
        public int Calls { get; private set; }
        public List<int> Limits { get; } = [];
        public Task<List<CheckoutCounter>> CountersByZone(Guid id, int limit, CancellationToken ct)
            => Read(store.Values.OfType<CheckoutCounter>().Where(x => x.ZoneId == id), limit);
        public Task<List<CameraCheckoutMapping>> MappingsByCounter(Guid id, int limit, CancellationToken ct)
            => Read(store.Values.OfType<CameraCheckoutMapping>().Where(x => x.CheckoutCounterId == id), limit);
        public Task<List<CameraCheckoutMapping>> MappingsByCamera(Guid id, int limit, CancellationToken ct)
            => Read(store.Values.OfType<CameraCheckoutMapping>().Where(x => x.CameraId == id), limit);
        private Task<List<T>> Read<T>(IEnumerable<T> rows, int limit)
        { Calls++; Limits.Add(limit); return Task.FromResult(rows.Take(limit + 1).ToList()); }
    }

    private sealed class MemoryStore : ISetupStore
    {
        public List<Entity> Values { get; } = [];
        public List<Type> RemovedTypes { get; } = [];
        public int Writes { get; private set; }
        public int Transactions { get; private set; }
        public bool FailOnAccess { get; set; }
        public Task<T?> Find<T>(Guid id, CancellationToken ct = default) where T : Entity, new()
        {
            Assert.False(FailOnAccess); return Task.FromResult(Values.OfType<T>().FirstOrDefault(e => Key(e) == id));
        }
        public Task<List<T>> List<T>(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) where T : Entity, new()
        {
            Assert.False(FailOnAccess); Assert.NotNull(filter);
            Assert.NotEqual(typeof(CheckoutCounter), typeof(T)); Assert.NotEqual(typeof(CameraCheckoutMapping), typeof(T));
            return Task.FromResult(Values.OfType<T>().Where(filter.Compile()).ToList());
        }
        public Task Add<T>(T entity, CancellationToken ct = default) where T : Entity, new()
        {
            Assert.False(FailOnAccess); Writes++;
            if (entity is TrackedEntity tracked) tracked.CreatedAt = tracked.UpdatedAt = Version;
            Values.Add(entity); return Task.CompletedTask;
        }
        public Task Update<T>(T entity, CancellationToken ct = default) where T : Entity, new()
        {
            Assert.False(FailOnAccess); Writes++;
            var index = Values.FindIndex(e => e is T && Key(e) == Key(entity)); Assert.True(index >= 0);
            if (entity is TrackedEntity tracked) tracked.UpdatedAt = tracked.UpdatedAt.AddMilliseconds(1);
            Values[index] = entity; return Task.CompletedTask;
        }
        public Task Remove<T>(T entity, CancellationToken ct = default) where T : Entity, new()
        {
            Assert.False(FailOnAccess); Writes++; RemovedTypes.Add(typeof(T));
            Values.RemoveAll(e => e is T && Key(e) == Key(entity)); return Task.CompletedTask;
        }
        public Task<T> Transaction<T>(Func<Task<T>> work, CancellationToken ct = default)
        { Assert.False(FailOnAccess); Transactions++; return work(); }
        private static Guid Key(Entity entity) => (Guid)entity.GetType().GetProperties().First(p => p.Name.EndsWith("Id")).GetValue(entity)!;
    }
}
