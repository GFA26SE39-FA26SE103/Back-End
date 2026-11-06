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
    public async Task SaveRequiresAnApprovedSlaPolicyInsteadOfInventingPriorityAndTiming()
    {
        var h = World();
        h.Store.Values.RemoveAll(e => e is SlaPolicy);
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        var policies = new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN"));

        Assert.Equal("POLICY_MISSING",
            (await Assert.ThrowsAsync<AppError>(() => policies.Save(rule.RuleId, "CRITICAL", new(true, null, new(true, null, null, "TT-01")), default))).Code);
        Assert.Empty(h.Store.Values.OfType<SlaPolicy>());
    }

    [Fact]
    public async Task SaveCreatesACriticalAutomaticTaskActionInSlaStorage()
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        var policies = new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN"));

        var saved = await policies.Save(rule.RuleId, "critical",
            new ResponsePolicySaveRequest(true, 1, new(true, "  Check congestion  ", "  Help customers move safely.  ", "TT-01")), default);

        Assert.Equal("CRITICAL", saved.EventPriority);
        Assert.True(saved.IsEnabled);
        Assert.Equal(2, saved.Version);
        Assert.True(saved.ReadyForAutomaticTask);
        Assert.Empty(saved.Issues);
        var policy = Assert.Single(h.Store.Values.OfType<SlaPolicy>());
        var action = Assert.Single(h.Store.Values.OfType<SlaPolicyAction>());
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
        var store = new DeferredWriteStore(config, rule, ApprovedPolicy(rule.EventTypeId!.Value), DefaultTaskType());
        var policies = new OperationalResponsePolicies(store, new MonitoringSetupTests.User("ADMIN"));

        var saved = await policies.Save(rule.RuleId, "CRITICAL", new(true, 1, new(true, "Check congestion", null, "TT-01")), default);

        Assert.True(saved.ReadyForAutomaticTask);
        Assert.NotNull(saved.Action);
        Assert.Single(store.Values.OfType<SlaPolicy>());
        Assert.Single(store.Values.OfType<SlaPolicyAction>());
    }

    [Fact]
    public async Task RepeatedSaveUpdatesTheSamePolicyAndActionWithOptimisticVersioning()
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        var policies = new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN"));
        var created = await policies.Save(rule.RuleId, "CRITICAL", new(true, 1, new(true, "First", null, "TT-01")), default);

        var updated = await policies.Save(rule.RuleId, "CRITICAL", new(true, created.Version, new(true, "Second", "Inspect aisle", "TT-01")), default);

        Assert.Equal(created.PolicyId, updated.PolicyId);
        var createdAction = Assert.IsType<AutomaticTaskPolicyView>(created.Action);
        var updatedAction = Assert.IsType<AutomaticTaskPolicyView>(updated.Action);
        Assert.Equal(createdAction.ActionId, updatedAction.ActionId);
        Assert.Equal(3, updated.Version);
        Assert.Single(h.Store.Values.OfType<SlaPolicy>());
        Assert.Single(h.Store.Values.OfType<SlaPolicyAction>());
        Assert.Equal("POLICY_VERSION_CONFLICT",
            (await Assert.ThrowsAsync<AppError>(() => policies.Save(rule.RuleId, "CRITICAL", new(true, 1, new(true, "Stale", null, "TT-01")), default))).Code);
    }

    [Fact]
    public async Task SavePersistsAnExplicitTaskTypeForSpecialtyAwareDispatch()
    {
        var h = World();
        var specialty = new StaffSpecialty { SpecialtyId = Guid.NewGuid(), Code = "SPC-01", Name = "Checkout Operations" };
        h.Store.Values.Add(specialty);
        h.Store.Values.Add(new TaskType { TaskTypeId = Guid.NewGuid(), Code = "TT-03", Name = "Crowd control",
            SourceCategory = "Overcrowding", RequiredSpecialtyId = specialty.SpecialtyId });
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        var policies = new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN"));

        var saved = await policies.Save(rule.RuleId, "CRITICAL",
            new(true, 1, new(true, "Relieve congestion", null, "tt-03")), default);

        Assert.Equal("TT-03", saved.Action!.TaskTypeCode);
        using var json = JsonDocument.Parse(Assert.Single(h.Store.Values.OfType<SlaPolicyAction>()).ActionParametersJson!);
        Assert.Equal("TT-03", json.RootElement.GetProperty("taskTypeCode").GetString());
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

        var saved = await policies.Save(rule.RuleId, "CRITICAL", new(false, 1, new(false, null, null, "TT-01")), default);

        Assert.False(saved.ReadyForAutomaticTask);
        Assert.Equal(["RULE_DISABLED", "CONFIGURATION_NOT_ACTIVE", "ACTION_DISABLED"], saved.Issues);
    }

    [Fact]
    public async Task ReadinessExplainsMissingNonAutomaticAndAmbiguousActions()
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        var policy = Assert.Single(h.Store.Values.OfType<SlaPolicy>());
        var policies = new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN"));

        Assert.Equal(["ACTION_MISSING"], (await policies.List(rule.RuleId, default)).Single(v => v.EventPriority == "CRITICAL").Issues);
        h.Store.Values.Add(new SlaPolicyAction { ActionId = Guid.NewGuid(), PolicyId = policy.PolicyId, ActionType = "CREATE_OPERATIONAL_TASK", ConditionJson = "{\"alert_level\":\"CRITICAL\"}",
            ExecutionMode = "APPROVAL_REQUIRED", ActionParametersJson = "{\"taskTypeCode\":\"TT-01\"}", IsEnabled = true });
        Assert.Equal(["ACTION_NOT_AUTOMATIC"], (await policies.List(rule.RuleId, default)).Single(v => v.EventPriority == "CRITICAL").Issues);
        h.Store.Values.Add(new SlaPolicyAction { ActionId = Guid.NewGuid(), PolicyId = policy.PolicyId, ActionType = "CREATE_OPERATIONAL_TASK", ConditionJson = "{\"alert_level\":\"CRITICAL\"}",
            ExecutionMode = "AUTOMATIC", ActionParametersJson = "{\"taskTypeCode\":\"TT-01\"}", IsEnabled = true });
        Assert.Equal(["ACTION_AMBIGUOUS"], (await policies.List(rule.RuleId, default)).Single(v => v.EventPriority == "CRITICAL").Issues);
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
            (await Assert.ThrowsAsync<AppError>(() => policies.Save(rule.RuleId, "CRITICAL", new(true, 1, new(true, "Task", null, "TT-01")), default))).Code);
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
            (await Assert.ThrowsAsync<DomainException>(() => policies.Save(rule.RuleId, "CRITICAL", new(true, 1, new(true, "Task", null, "TT-01")), default))).Code);
        Assert.Single(h.Store.Values.OfType<SlaPolicy>());
    }

    [Fact]
    public async Task DeleteRejectsAStaleVersionThenRemovesOnlyTheManagedAction()
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        var policies = new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN"));
        var created = await policies.Save(rule.RuleId, "CRITICAL", new(true, 1, new(true, "Task", null, "TT-01")), default);

        Assert.Equal("POLICY_VERSION_CONFLICT",
            (await Assert.ThrowsAsync<AppError>(() => policies.Delete(rule.RuleId, "CRITICAL", new(created.Version + 1), default))).Code);
        Assert.Single(h.Store.Values.OfType<SlaPolicy>());
        Assert.Single(h.Store.Values.OfType<SlaPolicyAction>());
        await policies.Delete(rule.RuleId, "CRITICAL", new(created.Version), default);

        Assert.Single(h.Store.Values.OfType<SlaPolicy>());
        Assert.Empty(h.Store.Values.OfType<SlaPolicyAction>());
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

        var response = await client.PutAsJsonAsync($"/api/monitoring-rules/{rule.RuleId}/response-policies/CRITICAL",
            new ResponsePolicySaveRequest(true, 1, new(true, "Check congestion", "Inspect the zone", "TT-01")));

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0, h.Store.Values.OfType<SlaPolicyAction>().Count());
        if (expected == HttpStatusCode.OK)
            Assert.True((await response.Content.ReadFromJsonAsync<ResponsePolicyView>())!.ReadyForAutomaticTask);
    }

    [Fact]
    public async Task AdminHttpContractListsAndVersionChecksUpdatesAndDeletes()
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        var policies = new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN"));
        var created = await policies.Save(rule.RuleId, "CRITICAL", new(true, 1, new(true, "Task", null, "TT-01")), default);
        await using var factory = new SetupOverviewTests.Factory(h.Store);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "ADMIN");
        var route = $"/api/monitoring-rules/{rule.RuleId}/response-policies";

        var listed = await (await client.GetAsync(route)).Content.ReadFromJsonAsync<List<ResponsePolicyView>>();
        Assert.Equal(2, listed!.Count);
        Assert.All(listed, v => Assert.Equal(created.PolicyId, v.PolicyId));

        var staleUpdate = await client.PutAsJsonAsync($"{route}/CRITICAL", new ResponsePolicySaveRequest(true, created.Version + 1, new(true, "Changed", null, "TT-01")));
        Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);
        Assert.Contains("POLICY_VERSION_CONFLICT", await staleUpdate.Content.ReadAsStringAsync());

        using (var staleDelete = new HttpRequestMessage(HttpMethod.Delete, $"{route}/CRITICAL") { Content = JsonContent.Create(new ResponsePolicyDeleteRequest(created.Version + 1)) })
        {
            var response = await client.SendAsync(staleDelete);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("POLICY_VERSION_CONFLICT", await response.Content.ReadAsStringAsync());
        }
        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"{route}/CRITICAL") { Content = JsonContent.Create(new ResponsePolicyDeleteRequest(created.Version)) };
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(delete)).StatusCode);
    }

    [Fact]
    public async Task WarningAndCriticalSharePolicyAndVersionButUseDifferentConditionScopedActions()
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        var policies = new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN"));
        var warning = await policies.Save(rule.RuleId, "warning", new(true, 1, new(true, "Warn operator", null, "TT-01")), default);
        var critical = await policies.Save(rule.RuleId, "CRITICAL", new(true, warning.Version, new(true, "Task", null, "TT-01")), default);

        Assert.False(warning.ReadyForAutomaticTask);
        Assert.Empty(warning.Issues);
        Assert.Equal("NOTIFY_OPERATOR", warning.Action!.ActionType);
        Assert.True(critical.ReadyForAutomaticTask);
        Assert.Equal(warning.PolicyId, critical.PolicyId);
        var actions = h.Store.Values.OfType<SlaPolicyAction>().ToList();
        Assert.Equal(2, actions.Count);
        Assert.Equal("WARNING", OperationalSlaPolicyRules.AlertLevel(actions.Single(a => a.ActionId == warning.Action.ActionId).ConditionJson));
        Assert.Equal("CRITICAL", OperationalSlaPolicyRules.AlertLevel(actions.Single(a => a.ActionId == critical.Action!.ActionId).ConditionJson));
        Assert.Equal("POLICY_VERSION_CONFLICT",
            (await Assert.ThrowsAsync<AppError>(() => policies.Save(rule.RuleId, "WARNING", new(true, warning.Version, new(true, "Stale", null, "TT-01")), default))).Code);

        await policies.Delete(rule.RuleId, "WARNING", new(critical.Version), default);
        var policy = Assert.Single(h.Store.Values.OfType<SlaPolicy>());
        Assert.True(policy.IsEnabled);
        Assert.Equal(critical.Version + 1, policy.Version);
        Assert.Equal(critical.Action!.ActionId, Assert.Single(h.Store.Values.OfType<SlaPolicyAction>()).ActionId);
        Assert.True((await policies.List(rule.RuleId, default)).Single(v => v.EventPriority == "CRITICAL").ReadyForAutomaticTask);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task CompatibilityFlagsDisableOnlyTheSelectedActionAndNeverToggleSharedPolicy(bool entryEnabled, bool actionEnabled)
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        var policies = new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN"));
        var critical = await policies.Save(rule.RuleId, "CRITICAL", new(true, 1, new(true, "Task", null, "TT-01")), default);

        var warning = await policies.Save(rule.RuleId, "WARNING", new(entryEnabled, critical.Version, new(actionEnabled, null, null)), default);

        Assert.False(warning.IsEnabled);
        Assert.False(warning.Action!.IsEnabled);
        Assert.Equal(["ACTION_DISABLED"], warning.Issues);
        Assert.True(Assert.Single(h.Store.Values.OfType<SlaPolicy>()).IsEnabled);
        Assert.True(h.Store.Values.OfType<SlaPolicyAction>().Single(a => a.ActionId == critical.Action!.ActionId).IsEnabled);
    }

    [Fact]
    public async Task SavePreservesApprovedSlaPriorityAndTargetsAndWorksAcrossRulesOfTheSameEventType()
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        var anotherRule = new MonitoringRule { RuleId = Guid.NewGuid(), ConfigId = rule.ConfigId, EventTypeId = rule.EventTypeId, OutcomeType = "OPERATIONAL_EVENT", Enabled = true };
        h.Store.Values.Add(anotherRule);
        var policies = new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN"));

        var created = await policies.Save(rule.RuleId, "CRITICAL", new(true, 1, new(true, "First", null, "TT-01")), default);
        var updated = await policies.Save(anotherRule.RuleId, "CRITICAL", new(true, created.Version, new(true, "Second", null, "TT-01")), default);

        Assert.Equal(created.PolicyId, updated.PolicyId);
        Assert.Equal(created.Action!.ActionId, updated.Action!.ActionId);
        Assert.Equal(anotherRule.RuleId, updated.RuleId);
        var policy = Assert.Single(h.Store.Values.OfType<SlaPolicy>());
        Assert.Equal(("HIGH", 180, 1800, 45, 80), (policy.PriorityCode, policy.ResponseTargetSec, policy.ResolutionTargetSec, policy.OfferTimeoutSec, policy.WarningPercent));
    }

    [Fact]
    public async Task DisabledPolicyCanBeInspectedButSavingAnActionDoesNotEnableIt()
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        var policy = Assert.Single(h.Store.Values.OfType<SlaPolicy>());
        policy.IsEnabled = false;

        var saved = await new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN"))
            .Save(rule.RuleId, "CRITICAL", new(true, 1, new(true, null, null, "TT-01")), default);

        Assert.False(policy.IsEnabled);
        Assert.False(saved.ReadyForAutomaticTask);
        Assert.Contains("POLICY_DISABLED", saved.Issues);
    }

    [Fact]
    public async Task DuplicateActivePoliciesFailClosedRegardlessOfSlaPriority()
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        var duplicate = ApprovedPolicy(rule.EventTypeId!.Value);
        duplicate.PriorityCode = "LOW";
        h.Store.Values.Add(duplicate);
        var policies = new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN"));

        Assert.Equal("POLICY_AMBIGUOUS", (await Assert.ThrowsAsync<AppError>(() => policies.List(rule.RuleId, default))).Code);
        Assert.Equal("POLICY_AMBIGUOUS", (await Assert.ThrowsAsync<AppError>(() => policies.Save(rule.RuleId, "CRITICAL", new(true, 1, new(true, null, null, "TT-01")), default))).Code);
        Assert.Equal("POLICY_AMBIGUOUS", (await Assert.ThrowsAsync<AppError>(() => policies.Delete(rule.RuleId, "CRITICAL", new(1), default))).Code);
        Assert.All(h.Store.Values.OfType<SlaPolicy>(), p => Assert.Equal(1, p.Version));
    }

    [Fact]
    public async Task ActivePolicyIsSelectedByEventTypeGuidNotRuleOrSlaPriorityAndIgnoresUnrelatedPolicies()
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        var selected = Assert.Single(h.Store.Values.OfType<SlaPolicy>());
        var disabled = ApprovedPolicy(rule.EventTypeId!.Value);
        disabled.IsEnabled = false;
        var incident = ApprovedPolicy(rule.EventTypeId.Value);
        incident.ProblemDomain = "INCIDENT";
        var unrelated = ApprovedPolicy(Guid.NewGuid());
        h.Store.Values.AddRange([disabled, incident, unrelated]);

        var saved = await new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN"))
            .Save(rule.RuleId, "CRITICAL", new(true, 1, new(true, null, null, "TT-01")), default);

        Assert.Equal(selected.PolicyId, saved.PolicyId);
        Assert.All(new[] { disabled, incident, unrelated }, p => Assert.Equal(1, p.Version));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"alert_level\":\"CRITICAL\",\"extra\":true}")]
    public async Task MalformedOrUnsupportedConditionsDoNotAppearReady(string condition)
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        h.Store.Values.Add(new SlaPolicyAction { ActionId = Guid.NewGuid(), PolicyId = Assert.Single(h.Store.Values.OfType<SlaPolicy>()).PolicyId,
            ConditionJson = condition, ActionType = "CREATE_OPERATIONAL_TASK", ExecutionMode = "AUTOMATIC", ActionParametersJson = "{\"taskTypeCode\":\"TT-01\"}", IsEnabled = true });

        Assert.All(await new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN")).List(rule.RuleId, default), view =>
        {
            Assert.False(view.ReadyForAutomaticTask);
            Assert.Contains("ACTION_MISSING", view.Issues);
        });
    }

    [Fact]
    public async Task EnabledCriticalSaveRequiresAnExplicitTaskTypeBeforeMutatingPolicyOrAction()
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        var policies = new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN"));

        Assert.Equal("INVALID_TASK_TYPE", (await Assert.ThrowsAsync<DomainException>(() =>
            policies.Save(rule.RuleId, "CRITICAL", new(true, 1, new(true, "Task", null)), default))).Code);
        Assert.Equal(1, Assert.Single(h.Store.Values.OfType<SlaPolicy>()).Version);
        Assert.Empty(h.Store.Values.OfType<SlaPolicyAction>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("{\"taskTypeCode\":\"   \"}")]
    [InlineData("not-json")]
    public async Task CriticalReadinessRequiresExplicitTaskTypeInParameters(string? parameters)
    {
        var h = World();
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        h.Store.Values.Add(new SlaPolicyAction { ActionId = Guid.NewGuid(), PolicyId = Assert.Single(h.Store.Values.OfType<SlaPolicy>()).PolicyId,
            ConditionJson = "{\"alert_level\":\"CRITICAL\"}", ActionType = "CREATE_OPERATIONAL_TASK", ExecutionMode = "AUTOMATIC",
            ActionParametersJson = parameters, IsEnabled = true });

        var critical = (await new OperationalResponsePolicies(h.Store, new MonitoringSetupTests.User("ADMIN")).List(rule.RuleId, default))
            .Single(v => v.EventPriority == "CRITICAL");
        Assert.False(critical.ReadyForAutomaticTask);
        Assert.Equal(["TASK_TYPE_MISSING"], critical.Issues);
    }

    private static MonitoringSetupTests.Harness World()
    {
        var h = new MonitoringSetupTests.Harness();
        h.AddRule("OVERCROWDING_CONGESTION", "PEOPLE_COUNT");
        h.Configuration.Status = "ACTIVE";
        h.Store.Values.Add(DefaultTaskType());
        h.Store.Values.Add(ApprovedPolicy(Assert.Single(h.Store.Values.OfType<MonitoringRule>()).EventTypeId!.Value));
        return h;
    }

    private static TaskType DefaultTaskType() => new() { TaskTypeId = Guid.NewGuid(), Code = "TT-01", Name = "Queue assistance" };

    private static SlaPolicy ApprovedPolicy(Guid eventTypeId) => new()
    {
        PolicyId = Guid.NewGuid(), ProblemDomain = "OPERATIONAL_EVENT", EventTypeId = eventTypeId, IsEnabled = true, Version = 1,
        PriorityCode = "HIGH", ResponseTargetSec = 180, ResolutionTargetSec = 1800, OfferTimeoutSec = 45, WarningPercent = 80
    };

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
