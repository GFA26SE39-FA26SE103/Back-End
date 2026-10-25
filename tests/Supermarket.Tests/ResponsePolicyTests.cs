using System.Net;
using System.Net.Http.Json;
using System.Linq.Expressions;
using System.Text.Json;
using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Tests;

public sealed class ResponsePolicyTests
{
    [Fact]
    public async Task SaveCreatesOneAutomaticTaskActionForAnOperationalEventRule()
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        var policies = new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN"));

        var saved = await policies.Save(rule.RuleId, "warning",
            new ResponsePolicySaveRequest(true, null, new(true, "  Check congestion  ", "  Help customers move safely.  ")), default);

        Assert.Equal("WARNING", saved.EventPriority);
        Assert.True(saved.IsEnabled);
        Assert.Equal(1, saved.Version);
        Assert.True(saved.ReadyForAutomaticTask);
        Assert.Empty(saved.Issues);
        var policy = Assert.Single(h.Store.Values.OfType<ResponsePolicy>());
        var action = Assert.Single(h.Store.Values.OfType<ResponsePolicyAction>());
        Assert.Equal(policy.PolicyId, action.PolicyId);
        Assert.Equal("CREATE_OPERATIONAL_TASK", action.ActionType);
        Assert.Equal("AUTOMATIC", action.ExecutionMode);
        Assert.True(action.IsEnabled);
        using var json = JsonDocument.Parse(action.ActionParametersJson!);
        Assert.Equal("Check congestion", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("Help customers move safely.", json.RootElement.GetProperty("instructions").GetString());
    }

    [Fact]
    public async Task SaveResponseUsesTheRowsCreatedInsideTheTransactionWithoutRequeryingUnsavedData()
    {
        var config = new MonitoringConfiguration { ConfigId = Guid.NewGuid(), ZoneId = Guid.NewGuid(), Status = "ACTIVE" };
        var rule = new MonitoringRule { RuleId = Guid.NewGuid(), ConfigId = config.ConfigId, OutcomeType = "OPERATIONAL_EVENT", EventTypeId = Guid.NewGuid(), Enabled = true };
        var store = new DeferredWriteStore(config, rule);
        var policies = new OperationalResponsePolicies(store, new MonitoringSetupTests.User("ADMIN"));

        var saved = await policies.Save(rule.RuleId, "WARNING", new(true, null, new(true, "Check congestion", null)), default);

        Assert.True(saved.ReadyForAutomaticTask);
        Assert.NotNull(saved.Action);
        Assert.Single(store.Values.OfType<ResponsePolicy>());
        Assert.Single(store.Values.OfType<ResponsePolicyAction>());
    }

    [Fact]
    public async Task RepeatedSaveUpdatesTheSamePolicyAndActionWithOptimisticVersioning()
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        var policies = new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN"));
        var created = await policies.Save(rule.RuleId, "WARNING", new(true, null, new(true, "First", null)), default);

        var updated = await policies.Save(rule.RuleId, "WARNING", new(true, created.Version, new(true, "Second", "Inspect aisle")), default);

        Assert.Equal(created.PolicyId, updated.PolicyId);
        var createdAction = Assert.IsType<AutomaticTaskPolicyView>(created.Action);
        var updatedAction = Assert.IsType<AutomaticTaskPolicyView>(updated.Action);
        Assert.Equal(createdAction.ActionId, updatedAction.ActionId);
        Assert.Equal(2, updated.Version);
        Assert.Single(h.Store.Values.OfType<ResponsePolicy>());
        Assert.Single(h.Store.Values.OfType<ResponsePolicyAction>());
        Assert.Equal("POLICY_VERSION_CONFLICT",
            (await Assert.ThrowsAsync<AppError>(() => policies.Save(rule.RuleId, "WARNING", new(true, 1, new(true, "Stale", null)), default))).Code);
    }

    [Fact]
    public async Task ReadinessExplainsDisabledRuleConfigurationPolicyAndAction()
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        var config = Assert.Single(h.Store.Values.OfType<MonitoringConfiguration>());
        rule.Enabled = false;
        config.Status = "DRAFT";
        var policies = new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN"));

        var saved = await policies.Save(rule.RuleId, "CRITICAL", new(false, null, new(false, null, null)), default);

        Assert.False(saved.ReadyForAutomaticTask);
        Assert.Equal(["RULE_DISABLED", "CONFIGURATION_NOT_ACTIVE", "POLICY_DISABLED", "ACTION_DISABLED"], saved.Issues);
    }

    [Theory]
    [InlineData("OPERATOR")]
    [InlineData("MANAGER")]
    [InlineData("STAFF")]
    public async Task PolicyManagementRequiresAdmin(string role)
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        var policies = new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User(role));

        Assert.Equal("FORBIDDEN",
            (await Assert.ThrowsAsync<AppError>(() => policies.Save(rule.RuleId, "WARNING", new(true, null, new(true, "Task", null)), default))).Code);
    }

    [Fact]
    public async Task IncidentRulesCannotReceiveOperationalResponsePolicies()
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        rule.OutcomeType = "INCIDENT";
        rule.EventTypeId = null;
        rule.IncidentTypeId = Guid.NewGuid();
        var policies = new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN"));

        Assert.Equal("INVALID_POLICY_RULE",
            (await Assert.ThrowsAsync<DomainException>(() => policies.Save(rule.RuleId, "WARNING", new(true, null, new(true, "Task", null)), default))).Code);
        Assert.Empty(h.Store.Values.OfType<ResponsePolicy>());
    }

    [Fact]
    public async Task DeleteRemovesTheManagedActionAndPolicyOnlyAtTheExpectedVersion()
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        var policies = new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN"));
        var created = await policies.Save(rule.RuleId, "WARNING", new(true, null, new(true, "Task", null)), default);

        await policies.Delete(rule.RuleId, "WARNING", new(created.Version), default);

        Assert.Empty(h.Store.Values.OfType<ResponsePolicy>());
        Assert.Empty(h.Store.Values.OfType<ResponsePolicyAction>());
        Assert.Equal("NOT_FOUND",
            (await Assert.ThrowsAsync<AppError>(() => policies.Delete(rule.RuleId, "WARNING", new(created.Version), default))).Code);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("OPERATOR", HttpStatusCode.Forbidden)]
    [InlineData("MANAGER", HttpStatusCode.Forbidden)]
    [InlineData("STAFF", HttpStatusCode.Forbidden)]
    [InlineData("ADMIN", HttpStatusCode.OK)]
    public async Task SaveEndpointRequiresAdminAndReturnsTheManagedPolicy(string? role, HttpStatusCode expected)
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        await using var factory = new SetupOverviewTests.Factory(h.Store);
        using var client = factory.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);

        var response = await client.PutAsJsonAsync($"/api/monitoring-rules/{rule.RuleId}/response-policies/WARNING",
            new ResponsePolicySaveRequest(true, null, new(true, "Check congestion", "Inspect the zone")));

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0, h.Store.Values.OfType<ResponsePolicy>().Count());
        if (expected == HttpStatusCode.OK)
            Assert.True((await response.Content.ReadFromJsonAsync<ResponsePolicyView>())!.ReadyForAutomaticTask);
    }

    private static MonitoringSetupTests.Harness World()
    {
        var h = new MonitoringSetupTests.Harness();
        h.AddRule("OVERCROWDING_CONGESTION", "PEOPLE_COUNT");
        h.Configuration.Status = "ACTIVE";
        return h;
    }

    /// <summary>Models EfSetupStore's deferred SaveChanges visibility for newly added rows.</summary>
    private sealed class DeferredWriteStore(params Entity[] seed) : ISetupStore
    {
        public List<Entity> Values { get; } = [.. seed];
        private readonly List<Entity> pending = [];
        private bool inTransaction;
        public Task<T?> Find<T>(Guid id, CancellationToken ct = default) where T : Entity, new()
            => Task.FromResult(Values.OfType<T>().FirstOrDefault(e => (Guid)e.GetType().GetProperties().First(p => p.Name.EndsWith("Id")).GetValue(e)! == id));
        public Task<List<T>> List<T>(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) where T : Entity, new()
            => Task.FromResult(Values.OfType<T>().Where(filter?.Compile() ?? (_ => true)).ToList());
        public Task Add<T>(T entity, CancellationToken ct = default) where T : Entity, new()
        {
            (inTransaction ? pending : Values).Add(entity);
            return Task.CompletedTask;
        }
        public Task Update<T>(T entity, CancellationToken ct = default) where T : Entity, new() => Task.CompletedTask;
        public Task Remove<T>(T entity, CancellationToken ct = default) where T : Entity, new() { Values.Remove(entity); return Task.CompletedTask; }
        public async Task<T> Transaction<T>(Func<Task<T>> work, CancellationToken ct = default)
        {
            inTransaction = true;
            try
            {
                var result = await work();
                Values.AddRange(pending);
                pending.Clear();
                return result;
            }
            finally { inTransaction = false; }
        }
    }
}
