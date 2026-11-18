using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Supermarket.Application;
using Supermarket.Domain;
using Xunit;

namespace Supermarket.Tests;

public sealed class ManagerRecommendationTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
    private const string Route = "/api/manager/recommendations";
    private const string OperatorRoute = "/api/operator/recommendations";

    [Theory]
    [InlineData("success", null, HttpStatusCode.OK)]
    [InlineData("stale", "INSUFFICIENT_VALID_EVIDENCE", HttpStatusCode.OK)]
    [InlineData("changed", "EVIDENCE_CHANGED", HttpStatusCode.OK)]
    [InlineData("none", "NO_SUPPORTED_RECOMMENDATION", HttpStatusCode.OK)]
    [InlineData("invalid", null, HttpStatusCode.ServiceUnavailable)]
    public async Task GenerateRevalidatesEvidenceAndPersistsOnlySupportedAdvice(string scenario, string? reason, HttpStatusCode status)
    {
        var world = new World();
        var config = new MonitoringConfiguration { ConfigId = Guid.NewGuid(), ZoneId = world.Event.ZoneId, Status = "ACTIVE", UpdatedAt = Now.AddMinutes(-1) };
        var type = new OperationalEventType { EventTypeId = Guid.NewGuid(), Code = "LONG_QUEUE", MeasurementType = "QUEUE_LENGTH" };
        var rule = new MonitoringRule { RuleId = Guid.NewGuid(), ConfigId = config.ConfigId, EventTypeId = type.EventTypeId,
            ThresholdUnit = "PEOPLE", WarningThreshold = 5, CriticalThreshold = 10, UpdatedAt = config.UpdatedAt };
        world.Store.Values.AddRange([config, type, rule]);
        world.Event.RuleId = rule.RuleId;
        world.Event.EventTypeId = type.EventTypeId;
        world.Event.Priority = "WARNING";
        world.Event.MetricValue = 7;
        world.Event.MetadataJson = JsonSerializer.Serialize(new { configId = config.ConfigId, configurationVersion = config.UpdatedAt, metric = 7, thresholdUnit = "PEOPLE" });
        if (scenario == "stale") world.Event.LastObservedAt = Now.AddSeconds(-31);
        world.Model.OnChoose = () =>
        {
            if (scenario == "changed") world.Event.Status = "CLEARED";
            if (scenario == "success") world.Event.LastObservedAt = Now.AddSeconds(-1);
            return new(scenario switch { "none" => "NONE", "invalid" => "CREATE_TASK", _ => "REVIEW_CHECKOUT_CAPACITY" }, "HIGH");
        };
        await using var factory = world.Factory();
        using var client = ManagerClient(factory);
        using var response = await client.PostAsJsonAsync(Route + "/generate", new GenerateRecommendationRequest(world.Event.EventId));
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(scenario == "stale" ? 0 : 1, world.Model.Calls);
        if (status == HttpStatusCode.OK)
        {
            var result = (await response.Content.ReadFromJsonAsync<RecommendationGenerationResult>())!;
            Assert.Equal(reason, result.Reason);
            if (scenario == "success")
            {
                Assert.Equal("AVAILABLE", result.Recommendation!.Status);
                Assert.Equal("REVIEW_CHECKOUT_CAPACITY", result.Recommendation.RecommendationType);
                Assert.Equal(7, result.Recommendation.Evidence!.Signal.Metric);
                Assert.Contains("meeting the warning threshold of 5 people", result.Recommendation.Guidance!.Rationale);
                Assert.Equal(5, result.Recommendation.Guidance.RecheckAfterMinutes);
                Assert.Contains(result.Recommendation.Guidance.ImmediateSteps, step => step.Contains("staffed-checkout availability"));
                Assert.Contains("critical threshold of 10 people", result.Recommendation.Guidance.EscalationCondition);
                var detail = await client.GetFromJsonAsync<RecommendationView>($"{Route}/{result.Recommendation.RecommendationId}");
                Assert.Equal("HIGH", detail!.Evidence!.Confidence);
                Assert.Equal(result.Recommendation.Guidance.Rationale, detail.Guidance!.Rationale);
                Assert.Equal(result.Recommendation.Guidance.ImmediateSteps, detail.Guidance.ImmediateSteps);
                Assert.Equal(result.Recommendation.Guidance.RecheckAfterMinutes, detail.Guidance.RecheckAfterMinutes);
                Assert.Equal(result.Recommendation.Guidance.EscalationCondition, detail.Guidance.EscalationCondition);
                world.Event.LastObservedAt = Now;
                world.Event.UpdatedAt = Now;
                Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"{Route}/{result.Recommendation.RecommendationId}/decision", new RecommendationDecision("ACCEPTED"))).StatusCode);
            }
            else Assert.Null(result.Recommendation);
        }
        Assert.Equal(scenario == "success" ? 2 : 1, world.Store.Values.OfType<Recommendation>().Count());
        Assert.Empty(world.Store.Values.OfType<WorkTask>());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("OPERATOR", HttpStatusCode.Forbidden)]
    [InlineData("STAFF", HttpStatusCode.Forbidden)]
    [InlineData("ADMIN", HttpStatusCode.Forbidden)]
    public async Task RoutesRequireManager(string? role, HttpStatusCode expected)
    {
        var world = new World();
        await using var factory = world.Factory();
        using var client = factory.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        Assert.Equal(expected, (await client.GetAsync(Route)).StatusCode);
        Assert.Equal(expected, (await client.GetAsync(world.DetailPath)).StatusCode);
        Assert.Equal(expected, (await client.PostAsJsonAsync(world.DecisionPath, new RecommendationDecision("ACCEPTED"))).StatusCode);
        Assert.Equal(expected, (await client.PostAsJsonAsync(Route + "/generate", new GenerateRecommendationRequest(world.Event.EventId))).StatusCode);
        Assert.Empty(world.Store.Values.OfType<AuditLog>());
    }

    [Fact]
    public async Task ReadsAreScopedAndExpiredStatusIsEffective()
    {
        var world = new World();
        world.Row.ExpiresAt = Now;
        await using var factory = world.Factory();
        using var client = ManagerClient(factory);
        using var list = await client.GetAsync($"{Route}?status=expired&zoneId={world.Row.ZoneId}&page=2&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.True(list.Headers.CacheControl?.NoStore);
        Assert.Equal(world.Row.SupermarketId, world.Queries.SupermarketId);
        Assert.Equal(new RecommendationQuery("EXPIRED", world.Row.ZoneId, 2, 1), world.Queries.Query);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{Route}?pageSize=101")).StatusCode);
        var row = await client.GetFromJsonAsync<RecommendationView>(world.DetailPath);
        Assert.Equal("EXPIRED", row!.Status);
        Assert.Equal("AVAILABLE", world.Row.Status);
        world.Row.SupermarketId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(world.DetailPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(world.DecisionPath, new RecommendationDecision("DEFERRED", "Later"))).StatusCode);
    }

    [Fact]
    public async Task DeferredThenAcceptedAuditsBothDecisionsAndLeavesOperationalRecordsAlone()
    {
        var world = new World();
        await using var factory = world.Factory();
        using var client = ManagerClient(factory);
        using var defer = await client.PostAsJsonAsync(world.DecisionPath, new RecommendationDecision("DEFERRED", " Wait for review "));
        Assert.Equal(HttpStatusCode.OK, defer.StatusCode);
        var deferred = (await defer.Content.ReadFromJsonAsync<RecommendationView>())!;
        Assert.Equal("DEFERRED", deferred.Status);
        Assert.Equal(Now, deferred.ReviewedAt);
        var deferAudit = Assert.Single(world.Store.Values.OfType<AuditLog>());
        Assert.Equal((deferred.ReviewedByUserId, Now, "Wait for review"), (deferAudit.UserId, deferAudit.CreatedAt, deferAudit.Reason));
        Assert.Equal(world.Row.RecommendationId, deferAudit.EntityId);

        using var accept = await client.PostAsJsonAsync(world.DecisionPath, new RecommendationDecision("ACCEPTED"));
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.Equal("ACCEPTED", (await accept.Content.ReadFromJsonAsync<RecommendationView>())!.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(world.DecisionPath, new RecommendationDecision("ACCEPTED"))).StatusCode);
        var audits = world.Store.Values.OfType<AuditLog>().ToArray();
        Assert.Equal(2, audits.Length);
        Assert.Contains("\"status\":\"DEFERRED\"", audits[1].OldValuesJson);
        Assert.Contains("\"status\":\"ACCEPTED\"", audits[1].NewValuesJson);
        Assert.Equal(2, world.Store.Values.Count - world.SeededCount);
        Assert.Equal("ACTIVE", world.Event.Status);
    }

    [Fact]
    public async Task AcceptancePublishesOneInAppHandoffToActiveOperatorsOnly()
    {
        var world = new World();
        var operatorRole = new Role { RoleId = Guid.NewGuid(), Name = "OPERATOR" };
        var staffRole = new Role { RoleId = Guid.NewGuid(), Name = "STAFF" };
        var first = new UserAccount { UserId = Guid.NewGuid(), RoleId = operatorRole.RoleId, Status = "ACTIVE" };
        var second = new UserAccount { UserId = Guid.NewGuid(), RoleId = operatorRole.RoleId, Status = "ACTIVE" };
        world.Store.Values.AddRange([operatorRole, staffRole, first, second,
            new UserAccount { UserId = Guid.NewGuid(), RoleId = operatorRole.RoleId, Status = "DISABLED" },
            new UserAccount { UserId = Guid.NewGuid(), RoleId = staffRole.RoleId, Status = "ACTIVE" }]);
        await using var factory = world.Factory();
        using var manager = ManagerClient(factory);
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync(world.DecisionPath, new RecommendationDecision("DEFERRED", "Review later"))).StatusCode);
        Assert.Empty(world.Store.Values.OfType<Notification>());
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync(world.DecisionPath, new RecommendationDecision("ACCEPTED"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsJsonAsync(world.DecisionPath, new RecommendationDecision("ACCEPTED"))).StatusCode);

        var notification = Assert.Single(world.Store.Values.OfType<Notification>());
        Assert.Equal("RECOMMENDATION_ACCEPTED", notification.NotificationType);
        Assert.Equal(world.Event.EventId, notification.EventId);
        Assert.Null(notification.TaskId);
        Assert.Contains(world.Row.RecommendationId.ToString(), notification.Message);
        var recipients = world.Store.Values.OfType<NotificationRecipient>().ToArray();
        Assert.Equal(new[] { first.UserId, second.UserId }.Order(), recipients.Select(row => row.UserId).Order());
        Assert.All(recipients, row => Assert.Equal((notification.NotificationId, "IN_APP", "SENT"),
            (row.NotificationId, row.DeliveryChannel, row.DeliveryStatus)));
        Assert.Empty(world.Store.Values.OfType<WorkTask>());
    }

    [Fact]
    public async Task OperatorCanReadOnlyAcceptedRecommendationsInConfiguredStore()
    {
        var world = new World();
        await using var factory = world.Factory();
        using var op = factory.CreateClient();
        op.DefaultRequestHeaders.Add("X-Test-Role", "OPERATOR");
        using var manager = ManagerClient(factory);
        var detailPath = $"{OperatorRoute}/{world.Row.RecommendationId}";
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync(OperatorRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await op.GetAsync(detailPath)).StatusCode);
        using var list = await op.GetAsync($"{OperatorRoute}?status=AVAILABLE&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.True(list.Headers.CacheControl?.NoStore);
        Assert.Equal(new RecommendationQuery("ACCEPTED", PageSize: 10), world.Queries.Query);
        Assert.Equal(world.Row.SupermarketId, world.Queries.SupermarketId);

        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync(world.DecisionPath, new RecommendationDecision("ACCEPTED"))).StatusCode);
        var view = await op.GetFromJsonAsync<RecommendationView>(detailPath);
        Assert.Equal("ACCEPTED", view!.Status);
        Assert.Empty(world.Store.Values.OfType<Notification>());
        Assert.Equal(HttpStatusCode.Forbidden, (await op.PostAsJsonAsync(world.DecisionPath, new RecommendationDecision("DISMISSED", "Ignore"))).StatusCode);
        world.Row.SupermarketId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await op.GetAsync(detailPath)).StatusCode);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("changed")]
    [InlineData("cleared")]
    [InlineData("stale")]
    [InlineData("expired")]
    public async Task UnverifiableOrExpiredAcceptanceLeavesDecisionAndAuditUntouched(string change)
    {
        var world = new World();
        switch (change)
        {
            case "missing": world.Row.SupportingDataJson = null; break;
            case "malformed": world.Row.SupportingDataJson = "{\"eventEvidence\":[]}"; break;
            case "changed": world.Event.UpdatedAt = Now; break;
            case "cleared": world.Event.Status = "CLEARED"; break;
            case "stale": world.SetEvidence(Now); break;
            case "expired": world.Row.ExpiresAt = Now; break;
        }
        await using var factory = world.Factory();
        using var client = ManagerClient(factory);
        using var response = await client.PostAsJsonAsync(world.DecisionPath, new RecommendationDecision("ACCEPTED"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(change == "expired" ? "RECOMMENDATION_EXPIRED" : "RECOMMENDATION_EVIDENCE_UNAVAILABLE", await response.Content.ReadAsStringAsync());
        Assert.Equal("AVAILABLE", world.Row.Status);
        Assert.Empty(world.Store.Values.OfType<AuditLog>());
    }

    [Fact]
    public async Task SwaggerContainsRecommendationRoutes()
    {
        var world = new World();
        await using var factory = world.Factory();
        using var client = factory.CreateClient();
        var json = await client.GetStringAsync("/swagger/v1/swagger.json");
        using var document = JsonDocument.Parse(json);
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty(Route).TryGetProperty("get", out _));
        Assert.True(paths.GetProperty(Route + "/{id}").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty(Route + "/{id}/decision").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty(Route + "/generate").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty(OperatorRoute).TryGetProperty("get", out _));
        Assert.True(paths.GetProperty(OperatorRoute + "/{id}").TryGetProperty("get", out _));
        var guidance = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("RecommendationGuidance").GetProperty("properties");
        Assert.True(guidance.TryGetProperty("rationale", out _));
        Assert.True(guidance.TryGetProperty("immediateSteps", out _));
        Assert.True(guidance.TryGetProperty("recheckAfterMinutes", out _));
        Assert.True(guidance.TryGetProperty("escalationCondition", out _));
    }

    private static HttpClient ManagerClient(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "MANAGER");
        return client;
    }

    private sealed class World
    {
        public readonly MonitoringSetupTests.MemoryStore Store = new();
        public readonly QueriesStub Queries = new();
        public readonly ModelStub Model = new();
        public readonly Recommendation Row;
        public readonly OperationalEvent Event;
        public readonly int SeededCount;
        public string DetailPath => $"{Route}/{Row.RecommendationId}";
        public string DecisionPath => $"{DetailPath}/decision";

        public World()
        {
            var market = new Domain.Supermarket { SupermarketId = Guid.NewGuid() };
            var floor = new Floor { FloorId = Guid.NewGuid(), SupermarketId = market.SupermarketId };
            var zone = new Zone { ZoneId = Guid.NewGuid(), FloorId = floor.FloorId };
            Event = new() { EventId = Guid.NewGuid(), ZoneId = zone.ZoneId, Status = "ACTIVE", LastObservedAt = Now.AddSeconds(-2), UpdatedAt = Now.AddSeconds(-2) };
            Row = new() { RecommendationId = Guid.NewGuid(), SupermarketId = market.SupermarketId, ZoneId = zone.ZoneId,
                EventId = Event.EventId, GeneratedAt = Now.AddSeconds(-1), ExpiresAt = Now.AddMinutes(1) };
            SetEvidence(Now.AddSeconds(5));
            Store.Values.AddRange([market, floor, zone, Event, Row]);
            SeededCount = Store.Values.Count;
        }

        public void SetEvidence(DateTime validUntil) => Row.SupportingDataJson = JsonSerializer.Serialize(new
        {
            EventEvidence = new RecommendationEventEvidence(Event.EventId, Event.LastObservedAt, Event.UpdatedAt, validUntil)
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        public Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory() => new SetupOverviewTests.Factory(Store)
            .WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Swagger:Enabled"] = "true" }))
                .ConfigureTestServices(services =>
                {
                    services.RemoveAll<IClock>();
                    services.AddSingleton<IClock>(new StaffTaskTests.Clock { UtcNow = Now });
                    services.RemoveAll<IRecommendationQueries>();
                    services.AddSingleton<IRecommendationQueries>(Queries);
                    services.RemoveAll<IRecommendationModel>();
                    services.AddSingleton<IRecommendationModel>(Model);
                }));
    }

    private sealed class ModelStub : IRecommendationModel
    {
        public int Calls;
        public Func<RecommendationChoice> OnChoose = () => throw new InvalidOperationException();

        public Task<RecommendationChoice> Choose(RecommendationSignal signal, IReadOnlyList<string> actions, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(OnChoose());
        }
    }

    private sealed class QueriesStub : IRecommendationQueries
    {
        public RecommendationQuery? Query;
        public Guid SupermarketId;
        public Task<RecommendationPage> List(Guid supermarketId, RecommendationQuery query, DateTime now, CancellationToken ct)
        {
            SupermarketId = supermarketId;
            Query = query;
            return Task.FromResult(new RecommendationPage([], query.Page, query.PageSize, false));
        }
    }
}
