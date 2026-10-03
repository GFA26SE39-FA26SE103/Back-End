using System.Linq.Expressions;
using Supermarket.Application;
using Supermarket.Domain;
using AppError = Supermarket.Application.ApplicationException;
using Xunit;

namespace Supermarket.Tests;

public sealed class MonitoringSetupTests
{
    [Fact]
    public async Task PeopleCountModeDoesNotRequireArea()
    {
        var h=new Harness(); h.AddRule("OVERCROWDING_CONGESTION","CROWD_DENSITY","PEOPLE");
        h.Store.Values.OfType<MonitoringRule>().Single().ParametersJson="{\"measurementMode\":\"PEOPLE_COUNT\"}";
        h.Zone.AreaM2=null;
        var review=await h.Setup.Review(h.Zone.ZoneId,default);
        Assert.True(review.CanActivate); Assert.DoesNotContain(review.Issues,i=>i.Code=="ZONE_AREA_REQUIRED");
    }
    [Fact]
    public async Task ActivationRejectsMoreThanOneActiveMapping()
    {
        var h=new Harness(); h.AddRule();
        var second=new Camera { CameraId=Guid.NewGuid(), FloorId=h.Zone.FloorId, Status="ACTIVE" };
        h.Store.Values.Add(second);
        h.Store.Values.Add(new CameraZoneMapping { CameraZoneId=Guid.NewGuid(),CameraId=second.CameraId,ZoneId=h.Zone.ZoneId,RoiPolygon="[{\"x\":0,\"y\":0},{\"x\":1,\"y\":0},{\"x\":0,\"y\":1}]" });
        var review=await h.Setup.Review(h.Zone.ZoneId,default);
        Assert.False(review.CanActivate); Assert.Contains(review.Issues,i=>i.Code=="MEASUREMENT_SOURCE_AMBIGUOUS");
    }
    [Fact]
    public async Task ActivationRejectsConfigurationWithoutEnabledRules()
    {
        var h = new Harness();
        var error = await Assert.ThrowsAsync<AppError>(() => h.Setup.Activate(h.Zone.ZoneId, true, default));
        Assert.Equal("MONITORING_NOT_READY", error.Code);
        Assert.Equal("DRAFT", h.Configuration.Status);
    }

    [Fact]
    public async Task SavingActiveConfigurationIsRejectedWithoutChangingIt()
    {
        var h = new Harness();
        h.Configuration.Status = "ACTIVE";
        var error = await Assert.ThrowsAsync<AppError>(() => h.Setup.Save(h.Zone.ZoneId, new MonitoringRequest("Changed", .8m), default));
        Assert.Equal("MONITORING_ACTIVE", error.Code);
        Assert.Equal("Original", h.Configuration.Name);
        Assert.Equal(.5m, h.Configuration.ConfidenceThreshold);
        Assert.Equal("ACTIVE", h.Configuration.Status);
    }

    [Fact]
    public async Task ReviewChecksDensityAreaAndRechecksCurrentSourceAtActivation()
    {
        var h = new Harness();
        h.AddRule("OVERCROWDING_CONGESTION", "CROWD_DENSITY", "PEOPLE_PER_M2");
        h.Zone.AreaM2 = null;
        var review = await h.Setup.Review(h.Zone.ZoneId, default);
        Assert.False(review.CanActivate);
        Assert.Contains(review.Issues, i => i.Code == "ZONE_AREA_REQUIRED");
        h.Zone.AreaM2 = 10;
        var density=await h.Setup.Review(h.Zone.ZoneId, default);
        Assert.False(density.CanActivate); Assert.Contains(density.Issues,i=>i.Code=="RULE_RUNTIME_UNSUPPORTED");
        h.Connection.LastTestResult = "FAILED";
        var error = await Assert.ThrowsAsync<AppError>(() => h.Setup.Activate(h.Zone.ZoneId, true, h.Configuration.UpdatedAt, default));
        Assert.Equal("MONITORING_NOT_READY", error.Code);
        Assert.Equal("DRAFT", h.Configuration.Status);
    }

    [Fact]
    public async Task DraftSaveReplacesRulesRejectsStaleVersionAndActivatesOnlyValidSetup()
    {
        var h = new Harness();
        var type = h.AddRule();
        var input = new MonitoringRuleRequest(type.IncidentTypeId, 4, 7, "PEOPLE", 0, 0);
        var stale = await Assert.ThrowsAsync<AppError>(() => h.Setup.Save(h.Zone.ZoneId, new MonitoringRequest("Stale", Rules: [input], ExpectedUpdatedAt: DateTime.UnixEpoch), default));
        Assert.Equal("CONFIGURATION_CHANGED", stale.Code);
        var saved = await h.Setup.Save(h.Zone.ZoneId, new MonitoringRequest("Saved", .6m, [input], h.Configuration.UpdatedAt), default);
        Assert.Equal(4, Assert.Single(saved.Rules).WarningThreshold);
        var active = await h.Setup.Activate(h.Zone.ZoneId, true, saved.UpdatedAt, default);
        Assert.Equal("ACTIVE", active.Status);
        var inactive = await h.Setup.Activate(h.Zone.ZoneId, false, active.UpdatedAt, default);
        var empty = await Assert.ThrowsAsync<DomainException>(() => h.Setup.Save(h.Zone.ZoneId, new MonitoringRequest("Empty", Rules: [], ExpectedUpdatedAt: inactive.UpdatedAt), default));
        Assert.Equal("RULES_REQUIRED", empty.Code);
        var unchanged = await h.Setup.Get(h.Zone.ZoneId, default);
        Assert.Equal("Saved", unchanged.Name);
        Assert.Single(unchanged.Rules);
        Assert.Equal("INACTIVE", unchanged.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavingWithoutIncidentRulesDoesNotCreateConfiguration(bool omitRules)
    {
        var h = new Harness();
        h.Store.Values.Remove(h.Configuration);
        var request = new MonitoringRequest("Missing incidents", Rules: omitRules ? null : []);
        if (omitRules)
            Assert.Equal("RULES_REQUIRED", (await Assert.ThrowsAsync<AppError>(() => h.Setup.Save(h.Zone.ZoneId, request, default))).Code);
        else
            Assert.Equal("RULES_REQUIRED", (await Assert.ThrowsAsync<DomainException>(() => h.Setup.Save(h.Zone.ZoneId, request, default))).Code);
        Assert.Empty(h.Store.Values.OfType<MonitoringConfiguration>());
        Assert.Empty(h.Store.Values.OfType<MonitoringRule>());
    }

    [Theory]
    [InlineData("OPERATOR")]
    [InlineData("STAFF")]
    [InlineData("MANAGER")]
    public async Task ConfigurationIsAdminOnly(string role)
    {
        var h = new Harness(role);
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<AppError>(() => h.Setup.Get(h.Zone.ZoneId, default))).Code);
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<AppError>(() => h.Setup.IncidentTypes(default))).Code);
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<AppError>(() => h.Setup.Review(h.Zone.ZoneId, default))).Code);
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<AppError>(() => h.Setup.Save(h.Zone.ZoneId, new MonitoringRequest("No", Rules: []), default))).Code);
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<AppError>(() => h.Setup.Activate(h.Zone.ZoneId, true, default))).Code);
    }

    [Theory]
    [InlineData("roi")]
    [InlineData("camera")]
    [InlineData("cross-floor")]
    [InlineData("test")]
    [InlineData("disabled")]
    [InlineData("demo")]
    [InlineData("mapping")]
    [InlineData("zone")]
    public async Task InvalidReadinessBlocksActivation(string reason)
    {
        var h = new Harness(); h.AddRule();
        var mapping = h.Store.Values.OfType<CameraZoneMapping>().Single();
        switch (reason)
        {
            case "roi": mapping.RoiPolygon = "{broken"; break;
            case "camera": h.Camera.Status = "INACTIVE"; break;
            case "cross-floor": h.Camera.FloorId = Guid.NewGuid(); break;
            case "test": h.Connection.LastTestedAt = null; break;
            case "disabled": h.Connection.IsEnabled = false; break;
            case "demo": h.Connection.SourceType = "DEMO"; h.Connection.StreamUri = "demo://test"; break;
            case "mapping": mapping.Status = "INACTIVE"; break;
            case "zone": h.Zone.Status = "INACTIVE"; break;
        }
        Assert.False((await h.Setup.Review(h.Zone.ZoneId, default)).CanActivate);
        Assert.Equal("MONITORING_NOT_READY", (await Assert.ThrowsAsync<AppError>(() => h.Setup.Activate(h.Zone.ZoneId, true, default))).Code);
    }

    internal sealed class Harness
    {
        public Zone Zone { get; } = new() { ZoneId = Guid.NewGuid(), FloorId = Guid.NewGuid(), Name = "Queue zone", Status = "ACTIVE", AreaM2 = 10 };
        public MonitoringConfiguration Configuration { get; }
        public Camera Camera { get; }
        public CameraConnection Connection { get; }
        public MemoryStore Store { get; } = new();
        public MonitoringSetup Setup { get; }
        public Harness(string role = "ADMIN")
        {
            Configuration = new() { ConfigId = Guid.NewGuid(), ZoneId = Zone.ZoneId, Name = "Original", UpdatedAt = new DateTime(2026,10,3,0,0,0,DateTimeKind.Utc) };
            Camera = new() { CameraId = Guid.NewGuid(), FloorId = Zone.FloorId, Code = "TEST-CAM", Status = "ACTIVE" };
            Connection = new() { ConnectionId = Guid.NewGuid(), CameraId = Camera.CameraId, SourceType = "LIVE", Protocol = "HTTP", StreamUri = "http://camera/video", IsEnabled = true, LastTestResult = "SUCCESS", LastTestedAt = DateTime.UtcNow };
            Store.Values.AddRange([Zone, Configuration, Camera, Connection, new CameraZoneMapping { CameraZoneId = Guid.NewGuid(), CameraId = Camera.CameraId, ZoneId = Zone.ZoneId, RoiPolygon = System.Text.Json.JsonSerializer.Serialize(new Point[] { new(0,0),new(1,0),new(0,1) }) }]);
            Setup = new(Store, new User(role));
        }
        public IncidentType AddRule(string code = "LONG_QUEUE", string measurement = "QUEUE_LENGTH", string unit = "PEOPLE")
        {
            var type = new IncidentType { IncidentTypeId = Guid.NewGuid(), Code = code, Name = code, SourceType = "AI_DETECTED", MeasurementType = measurement };
            Store.Values.Add(type);
            Store.Values.Add(new MonitoringRule { RuleId = Guid.NewGuid(), ConfigId = Configuration.ConfigId, IncidentTypeId = type.IncidentTypeId, WarningThreshold = 3, CriticalThreshold = 5, ThresholdUnit = unit });
            return type;
        }
    }
    internal sealed class User(string role) : ICurrentUser { public Guid UserId { get; } = Guid.NewGuid(); public string Role => role; }
    internal sealed class MemoryStore : ISetupStore
    {
        public List<Entity> Values { get; } = [];
        public Task<T?> Find<T>(Guid id, CancellationToken ct = default) where T : Entity, new()
            => Task.FromResult(Values.OfType<T>().FirstOrDefault(e => (Guid)e.GetType().GetProperties().First(p => p.Name.EndsWith("Id")).GetValue(e)! == id));
        public Task<List<T>> List<T>(Expression<Func<T,bool>>? filter = null, CancellationToken ct = default) where T : Entity,new()
            => Task.FromResult(Values.OfType<T>().Where(filter?.Compile() ?? (_ => true)).ToList());
        public Task Add<T>(T e, CancellationToken ct = default) where T : Entity,new() { Values.Add(e); e.GetType().GetProperty("UpdatedAt")?.SetValue(e, DateTime.UtcNow); return Task.CompletedTask; }
        public Task Update<T>(T e, CancellationToken ct = default) where T : Entity,new()
        {
            var key = e.GetType().GetProperties().First(p => p.Name.EndsWith("Id"));
            var id = key.GetValue(e);
            var index = Values.FindIndex(x => x is T && Equals(key.GetValue(x), id));
            if (index >= 0) Values[index] = e;
            e.GetType().GetProperty("UpdatedAt")?.SetValue(e, DateTime.UtcNow);
            return Task.CompletedTask;
        }
        public Task Remove<T>(T e, CancellationToken ct = default) where T : Entity,new() { Values.Remove(e); return Task.CompletedTask; }
        public Task<T> Transaction<T>(Func<Task<T>> action, CancellationToken ct = default) => action();
    }
}
