using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Tests;

/// <summary>L7/L1: Admin-managed SLA timing for OperationalEvent and TaskType scopes, escalation routing and WARNING target role.</summary>
public sealed class ScopedSlaPolicyTests
{
    private static readonly DateTime Created = new(2026, 11, 11, 3, 0, 0, DateTimeKind.Utc);
    private static readonly MonitoringSetupTests.User Admin = new("ADMIN");

    [Fact]
    public async Task OperationalEventSlaIsCreatedOnceThenVersionedAndRejectsStaleOrInvalidInput()
    {
        var store = new MonitoringSetupTests.MemoryStore();
        var type = new OperationalEventType { EventTypeId = Guid.NewGuid(), Code = "LONG_QUEUE", Name = "Long Queue", MeasurementType = "QUEUE_LENGTH" };
        store.Values.Add(type);
        var policies = new OperationalEventSlaPolicies(store, Admin);

        var listed = Assert.Single(await policies.List(default));
        Assert.Null(listed.Policy);
        Assert.Equal(["POLICY_MISSING"], listed.Issues);

        var created = await policies.Save(type.EventTypeId, new(null, true, "high", 120, 900, 45, 80), default);
        Assert.Equal(1, created.Policy!.Version);
        Assert.Equal("HIGH", created.Policy.PriorityCode);
        Assert.Empty(created.Issues);
        var stored = Assert.Single(store.Values.OfType<SlaPolicy>());
        Assert.Equal(("OPERATIONAL_EVENT", (Guid?)type.EventTypeId, (Guid?)null, (Guid?)null), (stored.ProblemDomain, stored.EventTypeId, stored.IncidentTypeId, stored.TaskTypeId));

        var updated = await policies.Save(type.EventTypeId, new(1, true, "CRITICAL", 60, 600, 30, 70), default);
        Assert.Equal(2, updated.Policy!.Version);
        Assert.Single(store.Values.OfType<SlaPolicy>());

        Assert.Equal("SLA_POLICY_VERSION_CONFLICT", (await Assert.ThrowsAsync<AppError>(() => policies.Save(type.EventTypeId, new(1, true, "LOW", 60, 600, 30, 70), default))).Code);
        Assert.Equal("INVALID_SLA_PRIORITY", (await Assert.ThrowsAsync<DomainException>(() => policies.Save(type.EventTypeId, new(2, true, "P1", 60, 600, 30, 70), default))).Code);
        Assert.Equal("INVALID_SLA_TARGETS", (await Assert.ThrowsAsync<DomainException>(() => policies.Save(type.EventTypeId, new(2, true, "LOW", 600, 60, 30, 70), default))).Code);
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<AppError>(() => new OperationalEventSlaPolicies(store, new MonitoringSetupTests.User("OPERATOR")).List(default))).Code);
    }

    [Fact]
    public async Task TaskTypeSlaSavedByAdminIsTheOneAppliedToStandaloneTasks()
    {
        var store = new MonitoringSetupTests.MemoryStore();
        var type = new TaskType { TaskTypeId = Guid.NewGuid(), Code = "TT-11", Name = "Clean Affected Area", SourceCategory = "Cleanliness Problems" };
        store.Values.Add(type);
        var policies = new TaskTypeSlaPolicies(store, Admin);

        var saved = await policies.Save(type.TaskTypeId, new(null, true, "MEDIUM", 300, 3600, 60, 80), default);
        Assert.Equal("STANDALONE_TASK", Assert.Single(store.Values.OfType<SlaPolicy>()).ProblemDomain);
        Assert.Equal(saved.Policy, Assert.Single(await policies.List(default)).Policy);

        var clock = new StaffTaskTests.Clock { UtcNow = Created };
        var task = new WorkTask { TaskId = Guid.NewGuid(), OriginType = TaskOrigin.Manual, TaskTypeId = type.TaskTypeId };
        await new StandaloneTaskSlaPolicyService(store, clock).Apply(task, default);
        Assert.Equal(saved.Policy!.PolicyId, task.SlaPolicyId);
        Assert.Equal("MEDIUM", task.Priority);
        Assert.Equal(Created.AddSeconds(3600), task.ResolutionDeadlineAt);
    }

    [Fact]
    public async Task ConfiguredEscalationReplacesTheDefaultSlaRecipients()
    {
        var w = new MonitorWorld();
        var op = w.User("OPERATOR"); var manager = w.User("MANAGER");
        var policy = new SlaPolicy { PolicyId = Guid.NewGuid(), ProblemDomain = "INCIDENT", IncidentTypeId = Guid.NewGuid(), IsEnabled = true, Version = 3,
            PriorityCode = "HIGH", ResponseTargetSec = 600, ResolutionTargetSec = 3600, OfferTimeoutSec = 60, WarningPercent = 50 };
        w.Store.Values.Add(policy);
        var escalation = new SlaEscalationPolicies(w.Store, Admin);

        var defaults = await escalation.Get(policy.PolicyId, default);
        Assert.True(defaults.UsesDefault);
        Assert.Equal(["OPERATOR", "MANAGER"], defaults.BreachRoles);

        Assert.Equal("SLA_POLICY_VERSION_CONFLICT", (await Assert.ThrowsAsync<AppError>(() => escalation.Save(policy.PolicyId, new(1, ["MANAGER"], ["MANAGER"]), default))).Code);
        Assert.Equal("INVALID_SLA_ESCALATION", (await Assert.ThrowsAsync<DomainException>(() => escalation.Save(policy.PolicyId, new(3, ["STAFF"], ["MANAGER"]), default))).Code);

        var saved = await escalation.Save(policy.PolicyId, new(3, ["manager"], ["MANAGER", "OPERATOR"]), default);
        Assert.False(saved.UsesDefault);
        Assert.Equal(4, saved.Version);
        Assert.Equal(["MANAGER"], saved.WarningRoles);
        Assert.Equal(3, w.Store.Values.OfType<SlaPolicyAction>().Count());

        var task = w.Task(policy.PolicyId);
        w.Clock.UtcNow = Created.AddMinutes(5);
        await w.Monitor.Tick(default);
        Assert.Equal([manager], w.Recipients(SlaMonitor.ResponseWarning));
        w.Clock.UtcNow = Created.AddMinutes(10);
        await w.Monitor.Tick(default);
        Assert.Equal(new HashSet<Guid> { op, manager }, w.Recipients(SlaMonitor.ResponseBreach).ToHashSet());
        Assert.Equal(task.TaskId, w.Store.Values.OfType<Notification>().First().TaskId);

        // Saving again replaces, never duplicates, the escalation rows.
        await escalation.Save(policy.PolicyId, new(4, ["OPERATOR"], ["MANAGER"]), default);
        Assert.Equal(2, w.Store.Values.OfType<SlaPolicyAction>().Count());
    }

    [Fact]
    public async Task WarningNotificationCanTargetManagersButCriticalTaskCannotTargetARole()
    {
        var h = new MonitoringSetupTests.Harness();
        h.AddRule("OVERCROWDING_CONGESTION", "PEOPLE_COUNT");
        h.Configuration.Status = "ACTIVE";
        var manager = new Role { RoleId = Guid.NewGuid(), Name = "MANAGER" };
        h.Store.Values.AddRange([new Role { RoleId = Guid.NewGuid(), Name = "OPERATOR" }, manager,
            new TaskType { TaskTypeId = Guid.NewGuid(), Code = "TT-03", Name = "Redirect Customer Flow" }]);
        var rule = Assert.Single(h.Store.Values.OfType<MonitoringRule>());
        h.Store.Values.Add(new SlaPolicy { PolicyId = Guid.NewGuid(), ProblemDomain = "OPERATIONAL_EVENT", EventTypeId = rule.EventTypeId, IsEnabled = true, Version = 1,
            PriorityCode = "HIGH", ResponseTargetSec = 180, ResolutionTargetSec = 1800, OfferTimeoutSec = 45, WarningPercent = 80 });
        var policies = new OperationalResponsePolicies(h.Store, Admin);

        var warning = await policies.Save(rule.RuleId, "WARNING", new(true, 1, new(true, null, null, null, "manager")), default);
        Assert.Equal("MANAGER", warning.Action!.TargetRoleCode);
        Assert.Equal(manager.RoleId, Assert.Single(h.Store.Values.OfType<SlaPolicyAction>()).TargetRoleId);
        Assert.Equal("MANAGER", (await policies.List(rule.RuleId, default)).Single(p => p.EventPriority == "WARNING").Action!.TargetRoleCode);

        Assert.Equal("INVALID_TARGET_ROLE", (await Assert.ThrowsAsync<DomainException>(() =>
            policies.Save(rule.RuleId, "CRITICAL", new(true, 2, new(true, null, null, "TT-03", "MANAGER")), default))).Code);
        var critical = await policies.Save(rule.RuleId, "CRITICAL", new(true, 2, new(true, null, null, "TT-03")), default);
        Assert.Null(critical.Action!.TargetRoleCode);
    }

    private sealed class MonitorWorld
    {
        public MonitoringSetupTests.MemoryStore Store { get; } = new();
        public StaffTaskTests.Clock Clock { get; } = new() { UtcNow = Created };
        public SlaMonitor Monitor { get; }

        public MonitorWorld()
        {
            foreach (var role in new[] { "OPERATOR", "MANAGER", "STAFF" }) Store.Values.Add(new Role { RoleId = Guid.NewGuid(), Name = role });
            Monitor = new SlaMonitor(Store, Clock, new TaskNotifier(Store, Clock), new ReadySchema());
        }

        public Guid User(string role)
        {
            var user = new UserAccount { UserId = Guid.NewGuid(), RoleId = Store.Values.OfType<Role>().Single(r => r.Name == role).RoleId, FullName = role, Email = $"{Guid.NewGuid():N}@test", Status = "ACTIVE" };
            Store.Values.Add(user);
            return user.UserId;
        }

        public WorkTask Task(Guid policyId)
        {
            var task = new WorkTask { TaskId = Guid.NewGuid(), ZoneId = Guid.NewGuid(), Title = "Clean the spill", Priority = "HIGH", Status = WorkTaskStatus.Offered,
                OriginType = TaskOrigin.Incident, CreatedAt = Created, SlaPolicyId = policyId,
                SlaSnapshotJson = $$"""{"priorityCode":"HIGH","warningPercent":50,"appliedAt":"{{Created:O}}"}""",
                ResponseDeadlineAt = Created.AddSeconds(600), ResolutionDeadlineAt = Created.AddSeconds(3600) };
            Store.Values.Add(task);
            return task;
        }

        public Guid[] Recipients(string type)
        {
            var notification = Assert.Single(Store.Values.OfType<Notification>(), n => n.NotificationType == type);
            return Store.Values.OfType<NotificationRecipient>().Where(r => r.NotificationId == notification.NotificationId).Select(r => r.UserId).ToArray();
        }
    }

    private sealed class ReadySchema : ITaskSchema { public Task<bool> Ready(CancellationToken ct) => System.Threading.Tasks.Task.FromResult(true); }
}
