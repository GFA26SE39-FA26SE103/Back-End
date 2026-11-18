using System.Text.Json;
using Supermarket.Application;
using Supermarket.Domain;
using Xunit;

namespace Supermarket.Tests;

public sealed class OperationalSlaPolicyTests
{
    [Theory]
    [InlineData("{\"alert_level\":\"WARNING\"}", "WARNING")]
    [InlineData("{\"alert_level\":\"CRITICAL\"}", "CRITICAL")]
    [InlineData("{\"alert_level\":\"LOW\"}", null)]
    [InlineData("{\"alert_level\":\"WARNING\",\"unknown\":true}", null)]
    [InlineData("{\"alert_level\":\"WARNING\",\"alert_level\":\"CRITICAL\"}", null)]
    [InlineData("[]", null)]
    [InlineData("{", null)]
    [InlineData(null, null)]
    public void ActionConditionsAreStrict(string? json, string? expected)
        => Assert.Equal(expected, OperationalSlaPolicyRules.AlertLevel(json));

    [Fact]
    public async Task PolicyMatchesEventTypeRegardlessOfWarningOrCriticalAndNeverMatchesAnotherType()
    {
        var w = new World(); w.Policy.EventTypeId = Guid.NewGuid();
        await w.Dispatch.Raised(w.Event, null, default);
        Assert.Empty(w.Store.Values.OfType<WorkTask>());
        Assert.Equal("POLICY_MISSING", Assert.Single(w.Store.Values.OfType<Notification>()).Message);
    }

    [Fact]
    public async Task WarningNotifiesOnceThenCriticalCreatesOneTaskWithImmutableTiming()
    {
        var w = new World();
        await w.Dispatch.Raised(w.Event, null, default);
        await w.Dispatch.Raised(w.Event, "WARNING", default);
        Assert.Empty(w.Store.Values.OfType<WorkTask>());
        var notification = Assert.Single(w.Store.Values.OfType<Notification>());
        Assert.Equal(w.Event.EventId, notification.EventId);
        Assert.Equal("WARNING", notification.Severity);
        Assert.Single(w.Store.Values.OfType<NotificationRecipient>());
        w.Event.Priority = "CRITICAL";
        await w.Dispatch.Raised(w.Event, "WARNING", default);
        await w.Dispatch.Raised(w.Event, "CRITICAL", default);
        var task = Assert.Single(w.Store.Values.OfType<WorkTask>());
        Assert.Equal(w.Policy.PolicyId, task.SlaPolicyId);
        Assert.Equal(w.Clock.UtcNow.AddSeconds(180), task.ResponseDeadlineAt);
        Assert.Equal(w.Clock.UtcNow.AddSeconds(1800), task.ResolutionDeadlineAt);
        using var json = JsonDocument.Parse(task.SlaSnapshotJson!);
        Assert.Equal("HIGH", json.RootElement.GetProperty("priorityCode").GetString());
        w.Policy.OfferTimeoutSec = 99;
        Assert.Equal(45, await w.Dispatch.OfferTimeout(task, default));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    [InlineData("invalid-condition")]
    [InlineData("invalid-timing")]
    [InlineData("ambiguous-action")]
    [InlineData("approval-required")]
    [InlineData("wrong-action")]
    [InlineData("missing-task-type")]
    [InlineData("oversized-instructions")]
    public async Task BadConfigurationNotifiesOperatorWithoutFabricatingATask(string problem)
    {
        var w = new World(); w.Event.Priority = "CRITICAL";
        if (problem == "missing") w.Store.Values.Remove(w.Policy);
        if (problem == "ambiguous") w.Store.Values.Add(new SlaPolicy { PolicyId = Guid.NewGuid(), EventTypeId = w.Event.EventTypeId, ProblemDomain = "OPERATIONAL_EVENT", IsEnabled = true });
        if (problem == "invalid-condition") w.Store.Values.OfType<SlaPolicyAction>().Last().ConditionJson = "{}";
        if (problem == "invalid-timing") w.Policy.ResponseTargetSec = -1;
        var action = w.Store.Values.OfType<SlaPolicyAction>().Last();
        if (problem == "ambiguous-action") w.Store.Values.Add(new SlaPolicyAction { ActionId = Guid.NewGuid(), PolicyId = w.Policy.PolicyId,
            IsEnabled = true, ActionType = action.ActionType, ExecutionMode = "AUTOMATIC", ConditionJson = action.ConditionJson });
        if (problem == "approval-required") action.ExecutionMode = "APPROVAL_REQUIRED";
        if (problem == "wrong-action") action.ActionType = "NOTIFY_OPERATOR";
        if (problem == "missing-task-type") action.ActionParametersJson = "{}";
        if (problem == "oversized-instructions") action.ActionParametersJson = JsonSerializer.Serialize(new { taskTypeCode = "TT-01", instructions = new string('x', 4001) });
        await w.Dispatch.Raised(w.Event, null, default);
        await w.Dispatch.Raised(w.Event, "CRITICAL", default);
        Assert.Empty(w.Store.Values.OfType<WorkTask>());
        Assert.Single(w.Store.Values.OfType<Notification>());
        Assert.Single(w.Store.Values.OfType<NotificationRecipient>());
    }

    private sealed class World
    {
        public MonitoringSetupTests.MemoryStore Store { get; } = new();
        public Clock Clock { get; } = new();
        public SlaPolicy Policy { get; }
        public OperationalEvent Event { get; }
        public TaskDispatch Dispatch { get; }
        public World()
        {
            var zone = new Zone { ZoneId = Guid.NewGuid(), Name = "Checkout" };
            var config = new MonitoringConfiguration { ConfigId = Guid.NewGuid(), Status = "ACTIVE" };
            var type = new OperationalEventType { EventTypeId = Guid.NewGuid(), Code = "LONG_QUEUE", Name = "Long Queue" };
            var rule = new MonitoringRule { RuleId = Guid.NewGuid(), ConfigId = config.ConfigId, EventTypeId = type.EventTypeId, OutcomeType = "OPERATIONAL_EVENT", Enabled = true };
            Event = new OperationalEvent { EventId = Guid.NewGuid(), ZoneId = zone.ZoneId, EventTypeId = type.EventTypeId, RuleId = rule.RuleId, Priority = "WARNING" };
            Policy = new SlaPolicy { PolicyId = Guid.NewGuid(), Version = 1, IsEnabled = true, ProblemDomain = "OPERATIONAL_EVENT", EventTypeId = type.EventTypeId, PriorityCode = "HIGH", ResponseTargetSec = 180, ResolutionTargetSec = 1800, OfferTimeoutSec = 45, WarningPercent = 80 };
            var role = new Role { RoleId = Guid.NewGuid(), Name = "OPERATOR" };
            Store.Values.AddRange([zone, config, type, rule, Policy, role, new UserAccount { UserId = Guid.NewGuid(), RoleId = role.RoleId, Status = "ACTIVE" },
                new TaskType { TaskTypeId = Guid.NewGuid(), Code = "TT-01" },
                new SlaPolicyAction { ActionId = Guid.NewGuid(), PolicyId = Policy.PolicyId, IsEnabled = true, ExecutionMode = "AUTOMATIC", ActionType = "NOTIFY_OPERATOR", ConditionJson = "{\"alert_level\":\"WARNING\"}" },
                new SlaPolicyAction { ActionId = Guid.NewGuid(), PolicyId = Policy.PolicyId, IsEnabled = true, ExecutionMode = "AUTOMATIC", ActionType = "CREATE_OPERATIONAL_TASK", ConditionJson = "{\"alert_level\":\"CRITICAL\"}", ActionParametersJson = "{\"taskTypeCode\":\"TT-01\"}" }]);
            var notifier = new TaskNotifier(Store, Clock);
            Dispatch = new TaskDispatch(Store, new StaffRoster(Store, new StoreCalendar(StoreCalendar.Zone(null))), notifier,
                new TaskVerification(Store, Clock, notifier), Clock, new Schema());
        }
    }
    public sealed class Clock : IClock { public DateTime UtcNow => new(2026, 11, 6, 3, 0, 0, DateTimeKind.Utc); }
    private sealed class Schema : ITaskSchema { public Task<bool> Ready(CancellationToken ct) => Task.FromResult(true); }
}
