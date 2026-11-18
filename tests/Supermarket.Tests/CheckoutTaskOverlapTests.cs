using Supermarket.Application;
using Supermarket.Domain;
using Xunit;

namespace Supermarket.Tests;

public sealed class CheckoutTaskOverlapTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CoexistingCheckoutEventsCreateSeparateTasksEvenForTheSameZoneAndTaskType(bool capacityFirst)
    {
        var w = new World();
        var events = capacityFirst ? new[] { w.Capacity, w.Queue } : new[] { w.Queue, w.Capacity };

        // Simultaneous active episodes, dispatched serially in both orders. The memory fixture
        // characterizes event overlap, not SQL transaction isolation or concurrent replay races.
        Assert.Equal(w.Queue.Event.ZoneId, w.Capacity.Event.ZoneId);
        Assert.Equal(w.Queue.Event.DetectedAt, w.Capacity.Event.DetectedAt);
        Assert.NotEqual(w.Queue.Event.EventId, w.Capacity.Event.EventId);
        Assert.NotEqual(w.Queue.Event.EventTypeId, w.Capacity.Event.EventTypeId);
        Assert.NotEqual(w.Queue.Event.RuleId, w.Capacity.Event.RuleId);
        foreach (var source in events)
            await w.Dispatch.Raised(source.Event, null, default);

        Assert.Equal(2, w.Store.Values.OfType<WorkTask>().Count());
        foreach (var source in events)
        {
            var task = Assert.Single(w.Store.Values.OfType<WorkTask>(), t => t.EventId == source.Event.EventId);
            Assert.Equal(source.Policy.PolicyId, task.SlaPolicyId);
            Assert.Equal(w.TaskType.TaskTypeId, task.TaskTypeId);
            Assert.Equal(source.Event.ZoneId, task.ZoneId);
            Assert.Equal(TaskOrigin.OperationalEvent, task.OriginType);
            Assert.Equal(WorkTaskStatus.Offered, task.Status);
            Assert.Null(task.IncidentId);
            Assert.Null(task.StaffReportId);
            Assert.Equal(w.Clock.UtcNow.AddSeconds(source.Policy.ResponseTargetSec), task.ResponseDeadlineAt);
            Assert.Equal(w.Clock.UtcNow.AddSeconds(source.Policy.ResolutionTargetSec), task.ResolutionDeadlineAt);
            var offer = Assert.Single(w.Store.Values.OfType<TaskOffer>(), o => o.TaskId == task.TaskId);
            Assert.Equal(w.StaffId, offer.StaffUserId);
            Assert.Equal(TaskOfferStatus.Pending, offer.Status);
        }
    }

    [Theory]
    [InlineData("LONG_QUEUE")]
    [InlineData("CHECKOUT_CAPACITY")]
    public async Task ReplayingEitherCheckoutEventPreservesBothTasksAndDoesNotRepeatDispatch(string eventTypeCode)
    {
        var w = new World();
        await w.Dispatch.Raised(w.Queue.Event, null, default);
        await w.Dispatch.Raised(w.Capacity.Event, null, default);
        var tasks = w.Store.Values.OfType<WorkTask>().ToArray();
        Assert.Equal(2, tasks.Length);
        Assert.Equal(2, w.Store.Values.OfType<TaskOffer>().Count());
        var before = tasks.Select(t => (t.TaskId, t.EventId, t.Status, t.SlaPolicyId,
            t.SlaSnapshotJson, t.ResponseDeadlineAt, t.ResolutionDeadlineAt)).ToArray();
        var entities = w.Store.Values.ToArray();
        var ev = Assert.Single(w.Store.Values.OfType<OperationalEventType>(), t => t.Code == eventTypeCode);
        var replay = Assert.Single(w.Store.Values.OfType<OperationalEvent>(), e => e.EventTypeId == ev.EventTypeId);

        await w.Dispatch.Raised(replay, null, default);
        await w.Dispatch.Raised(replay, "CRITICAL", default);

        Assert.Equal(before, w.Store.Values.OfType<WorkTask>().Select(t => (t.TaskId, t.EventId, t.Status,
            t.SlaPolicyId, t.SlaSnapshotJson, t.ResponseDeadlineAt, t.ResolutionDeadlineAt)).ToArray());
        // No extra Task, offer, ring marker, notification or recipient is appended on replay.
        Assert.Equal(entities, w.Store.Values.ToArray());
    }

    [Theory]
    [InlineData("LONG_QUEUE")]
    [InlineData("CHECKOUT_CAPACITY")]
    public async Task DisabledCheckoutPolicyDoesNotBorrowTheOtherEventTypesEnabledPolicy(string disabledTypeCode)
    {
        var w = new World();
        var disabled = disabledTypeCode == "LONG_QUEUE" ? w.Queue : w.Capacity;
        var enabled = disabledTypeCode == "LONG_QUEUE" ? w.Capacity : w.Queue;
        disabled.Policy.IsEnabled = false;

        await w.Dispatch.Raised(disabled.Event, null, default);
        await w.Dispatch.Raised(enabled.Event, null, default);

        var task = Assert.Single(w.Store.Values.OfType<WorkTask>());
        Assert.Equal(enabled.Event.EventId, task.EventId);
        Assert.Equal(enabled.Policy.PolicyId, task.SlaPolicyId);
        var exception = Assert.Single(w.Store.Values.OfType<Notification>(),
            n => n.NotificationType == "OPERATIONAL_SLA_CONFIGURATION_EXCEPTION");
        Assert.Equal(disabled.Event.EventId, exception.EventId);
        Assert.Equal("POLICY_MISSING", exception.Message);
    }

    private sealed record Source(OperationalEvent Event, SlaPolicy Policy);

    private sealed class World
    {
        // Reuse the existing non-SQL store and deterministic operational-policy clock.
        public MonitoringSetupTests.MemoryStore Store { get; } = new();
        public OperationalSlaPolicyTests.Clock Clock { get; } = new();
        public TaskType TaskType { get; }
        public Guid StaffId { get; } = Guid.NewGuid();
        public Source Queue { get; }
        public Source Capacity { get; }
        public TaskDispatch Dispatch { get; }

        public World()
        {
            var calendar = new StoreCalendar(StoreCalendar.Zone(null));
            var zone = new Zone { ZoneId = Guid.NewGuid(), Name = "Checkout" };
            var config = new MonitoringConfiguration { ConfigId = Guid.NewGuid(), ZoneId = zone.ZoneId, Status = "ACTIVE" };
            var specialty = new StaffSpecialty { SpecialtyId = Guid.NewGuid(), Code = "SPC-01", Name = "Checkout Operations" };
            TaskType = new TaskType { TaskTypeId = Guid.NewGuid(), Code = "TT-01", Name = "Open Additional Checkout",
                RequiredSpecialtyId = specialty.SpecialtyId };
            var role = new Role { RoleId = Guid.NewGuid(), Name = "STAFF" };
            var shift = new Shift { ShiftId = Guid.NewGuid(), StartTime = new(8, 0), EndTime = new(16, 0) };
            var assignment = new ShiftAssignment { ShiftAssignmentId = Guid.NewGuid(), ShiftId = shift.ShiftId,
                UserId = StaffId, WorkDate = calendar.Today(Clock.UtcNow), CheckInAt = Clock.UtcNow.AddHours(-2) };
            // Same checked-in, specialty-qualified Zone Staff shape as StaffTaskTests.
            Store.Values.AddRange([zone, config, specialty, TaskType, role, shift, assignment,
                new UserAccount { UserId = StaffId, RoleId = role.RoleId, Status = "ACTIVE", SpecialtyId = specialty.SpecialtyId },
                new StaffZoneAssignment { StaffZoneAssignmentId = Guid.NewGuid(), ShiftAssignmentId = assignment.ShiftAssignmentId,
                    ZoneId = zone.ZoneId, AssignedAt = Clock.UtcNow.AddHours(-3) }]);
            Queue = AddSource("LONG_QUEUE", zone, config);
            Capacity = AddSource("CHECKOUT_CAPACITY", zone, config);
            var notifier = new TaskNotifier(Store, Clock);
            Dispatch = new TaskDispatch(Store, new StaffRoster(Store, calendar), notifier,
                new TaskVerification(Store, Clock, notifier), Clock, new Schema());
        }

        private Source AddSource(string code, Zone zone, MonitoringConfiguration config)
        {
            var type = new OperationalEventType { EventTypeId = Guid.NewGuid(), Code = code, Name = code };
            var rule = new MonitoringRule { RuleId = Guid.NewGuid(), ConfigId = config.ConfigId,
                EventTypeId = type.EventTypeId, OutcomeType = "OPERATIONAL_EVENT", Enabled = true };
            var ev = new OperationalEvent { EventId = Guid.NewGuid(), EventTypeId = type.EventTypeId,
                RuleId = rule.RuleId, ZoneId = zone.ZoneId, Priority = "CRITICAL", Status = "ACTIVE",
                DetectedAt = Clock.UtcNow, LastObservedAt = Clock.UtcNow };
            var policy = new SlaPolicy { PolicyId = Guid.NewGuid(), Version = 1, IsEnabled = true,
                ProblemDomain = "OPERATIONAL_EVENT", EventTypeId = type.EventTypeId, PriorityCode = "HIGH",
                ResponseTargetSec = 180, ResolutionTargetSec = 1800, OfferTimeoutSec = 45, WarningPercent = 80 };
            Store.Values.AddRange([type, rule, ev, policy,
                new SlaPolicyAction { ActionId = Guid.NewGuid(), PolicyId = policy.PolicyId, IsEnabled = true,
                    ExecutionMode = "AUTOMATIC", ActionType = "CREATE_OPERATIONAL_TASK",
                    ConditionJson = """{"alert_level":"CRITICAL"}""",
                    ActionParametersJson = """{"taskTypeCode":"TT-01"}""" }]);
            return new Source(ev, policy);
        }
    }

    private sealed class Schema : ITaskSchema
    {
        public Task<bool> Ready(CancellationToken ct) => Task.FromResult(true);
    }
}
