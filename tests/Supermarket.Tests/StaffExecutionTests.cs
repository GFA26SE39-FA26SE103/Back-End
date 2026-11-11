using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
using AppError = Supermarket.Application.ApplicationException;
namespace Supermarket.Tests;

/// <summary>Staff execution (owner B): own shifts, check-in and offer responses as the mobile app calls them.</summary>
public sealed class StaffExecutionTests
{
    // 10:00 store time (UTC+7); the day shift runs 08:00-16:00 local.
    private static readonly DateTime Now = new(2026, 10, 11, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RetryingMyOwnAcceptanceReturnsTheTaskInsteadOfOfferClosed()
    {
        var w = new World(); var a = w.Staff("An"); w.Policy();
        await w.Raise();
        var offer = w.OfferFor(a); var staff = w.StaffTasks(a);

        var first = (await staff.Respond(offer.OfferId, new(TaskResponse.HandleNow, null, null), default)).Task!;
        var retry = (await staff.Respond(offer.OfferId, new(TaskResponse.HandleNow, null, null), default)).Task!;

        Assert.Equal((first.TaskId, WorkTaskStatus.InProgress, a), (retry.TaskId, retry.Status, retry.AssigneeUserId));
        Assert.Equal(first.StartedAt, retry.StartedAt);
        // Declining a task I already hold is not a retry.
        Assert.Equal("OFFER_CLOSED", (await Assert.ThrowsAsync<AppError>(() => staff.Respond(offer.OfferId,
            new(TaskResponse.CannotHandle, null, TaskDeclineReason.Busy), default))).Code);
        Assert.Equal(a, Assert.Single(w.Store.Values.OfType<WorkTask>()).CurrentAssigneeUserId);
    }

    [Fact]
    public async Task ShiftsHideDraftsAndACancelledShiftCannotBeCheckedInto()
    {
        var w = new World();
        var a = w.Staff("An", checkedIn: false);
        var draft = w.Assignment(a, ShiftAssignmentStatus.Draft, new(2026, 10, 12));
        var cancelled = w.Assignment(a, ShiftAssignmentStatus.Cancelled, new(2026, 10, 13));
        var staff = w.StaffTasks(a);

        var shifts = await staff.Shifts(new(2026, 10, 10), new(2026, 10, 14), default);
        Assert.DoesNotContain(shifts, s => s.ShiftAssignmentId == draft);
        Assert.Equal(ShiftAssignmentStatus.Cancelled, Assert.Single(shifts, s => s.ShiftAssignmentId == cancelled).Status);
        Assert.Equal("NOT_FOUND", (await Assert.ThrowsAsync<AppError>(() => staff.CheckIn(draft, default))).Code);

        w.Clock.UtcNow = new DateTime(2026, 10, 13, 1, 0, 0, DateTimeKind.Utc); // 08:00 local on the cancelled day
        Assert.Equal("SHIFT_CANCELLED", (await Assert.ThrowsAsync<AppError>(() => staff.CheckIn(cancelled, default))).Code);
    }

    [Fact]
    public async Task OnlyTheAssigneeStartsAnAcceptedTask()
    {
        var w = new World(); var a = w.Staff("An"); var b = w.Staff("Binh"); w.Policy();
        await w.Raise();
        var task = (await w.StaffTasks(a).Respond(w.OfferFor(a).OfferId, new(TaskResponse.HandleLater, Now.AddMinutes(10), null), default)).Task!;

        Assert.Equal("NOT_FOUND", (await Assert.ThrowsAsync<AppError>(() => w.StaffTasks(b).Start(task.TaskId, default))).Code);
        Assert.Empty(await w.StaffTasks(b).Tasks(null, default));
        Assert.Equal("INVALID_STATUS", (await Assert.ThrowsAsync<DomainException>(() => w.StaffTasks(a).Tasks("DONE", default))).Code);
        Assert.Equal(WorkTaskStatus.InProgress, (await w.StaffTasks(a).Start(task.TaskId, default)).Status);
    }

    [Fact]
    public async Task DispatchDoesNotRepeatStaffAndIgnoresLegacyRetryCount()
    {
        // D1 replaces the fixed retry count with five topology rings and never repeats a Staff member.
        var w = new World(); var a = w.Staff("An"); var op = w.User("OPERATOR", "Olivia"); w.Policy(offerTimeoutSec: 10);
        w.Setting(TaskRules.RetryIntervalSetting, "10"); w.Setting(TaskRules.MaxRetryCountSetting, "2");
        await w.Raise();
        var task = Assert.Single(w.Store.Values.OfType<WorkTask>());

        var first = Assert.Single(await w.StaffTasks(a).Offers(true, default));
        Assert.Equal((1, 5, Now.AddSeconds(10)), (first.Attempt, first.MaxAttempts, first.NextAlertAt));
        w.Clock.UtcNow = Now.AddSeconds(9); Assert.Equal(0, await w.Tick());
        w.Clock.UtcNow = Now.AddSeconds(10); Assert.Equal(1, await w.Tick());
        w.Clock.UtcNow = Now.AddSeconds(20); Assert.Equal(0, await w.Tick());
        Assert.Single(w.Notifications(task, TaskNotifier.Offer));
        Assert.Equal(5, w.Notifications(task, TaskNotifier.DispatchRing).Count);
        Assert.Equal([op], w.Recipients(Assert.Single(w.Notifications(task, TaskNotifier.NeedsAssignment))));
        Assert.Equal((WorkTaskStatus.Offered, (Guid?)null), (task.Status, task.CurrentAssigneeUserId));
        Assert.Equal((TaskOfferStatus.Expired, "NO_RESPONSE"), (w.OfferFor(a).Status, w.OfferFor(a).DeclineReasonCode));
        Assert.Equal("OFFER_CLOSED", (await Assert.ThrowsAsync<AppError>(() => w.StaffTasks(a).Respond(w.OfferFor(a).OfferId, new(TaskResponse.HandleNow, null, null), default))).Code);
    }

    [Fact]
    public async Task ExhaustedRingsWaitForOperatorInsteadOfRestartingForLateCheckIn()
    {
        var w = new World(); var late = w.Staff("An", checkedIn: false); var op = w.User("OPERATOR", "Olivia"); w.Policy(offerTimeoutSec: 10);
        w.Setting(TaskRules.RetryIntervalSetting, "10"); w.Setting(TaskRules.MaxRetryCountSetting, "2");
        await w.Raise();
        var task = Assert.Single(w.Store.Values.OfType<WorkTask>());

        Assert.Empty(w.Store.Values.OfType<TaskAssignmentHistory>());
        Assert.Equal([op], w.Recipients(Assert.Single(w.Notifications(task, TaskNotifier.NeedsAssignment))));
        Assert.Empty(await w.StaffTasks(late).Offers(true, default));

        // Exhaustion is terminal for automatic dispatch; a late check-in does not restart ring 1.
        w.Store.Values.OfType<ShiftAssignment>().Single(s => s.UserId == late).CheckInAt = Now.AddSeconds(5);
        w.Clock.UtcNow = Now.AddSeconds(10); Assert.Equal(0, await w.Tick());
        Assert.Empty(await w.StaffTasks(late).Offers(true, default));
        Assert.Single(w.Store.Values.OfType<WorkTask>());
        Assert.Single(w.Notifications(task, TaskNotifier.NeedsAssignment));
    }

    private sealed class World
    {
        public MonitoringSetupTests.MemoryStore Store { get; } = new();
        public StaffTaskTests.Clock Clock { get; } = new() { UtcNow = Now };
        private readonly StoreCalendar calendar = new(StoreCalendar.Zone(null));
        private readonly Zone zone;
        private readonly Camera camera;
        private readonly Schema schema = new();
        private readonly StaffRoster roster;
        private readonly TaskDispatch dispatch;
        private readonly TaskViews views;
        private readonly Guid ruleId = Guid.NewGuid(), eventTypeId = Guid.NewGuid(), shiftId = Guid.NewGuid();

        public World()
        {
            var floor = Guid.NewGuid();
            zone = new Zone { ZoneId = Guid.NewGuid(), FloorId = floor, Code = "B", Name = "Zone B · Checkout" };
            camera = new Camera { CameraId = Guid.NewGuid(), FloorId = floor, Code = "CAM-03", Name = "Checkout 3" };
            foreach (var role in new[] { "ADMIN", "OPERATOR", "MANAGER", "STAFF" }) Store.Values.Add(new Role { RoleId = Guid.NewGuid(), Name = role });
            var configuration = new MonitoringConfiguration
            {
                ConfigId = Guid.NewGuid(), ZoneId = zone.ZoneId, Name = "Queue monitoring", Status = "ACTIVE"
            };
            var rule = new MonitoringRule
            {
                RuleId = ruleId, ConfigId = configuration.ConfigId, EventTypeId = eventTypeId,
                OutcomeType = "OPERATIONAL_EVENT", Enabled = true
            };
            var specialty = new StaffSpecialty { SpecialtyId = Guid.NewGuid(), Code = "SPC-01", Name = "Checkout Operations" };
            var taskType = new TaskType { TaskTypeId = Guid.NewGuid(), Code = "TT-01", Name = "Open Additional Checkout",
                SourceCategory = "Long queue", RequiredSpecialtyId = specialty.SpecialtyId };
            Store.Values.AddRange([zone, camera, configuration, rule, specialty, taskType,
                new OperationalEventType { EventTypeId = eventTypeId, Code = "LONG_QUEUE", Name = "Long queue", MeasurementType = "QUEUE_LENGTH" },
                new Shift { ShiftId = shiftId, WeekStart = new(2026, 10, 5), Name = "Morning", StartTime = new(8, 0), EndTime = new(16, 0) }]);
            roster = new StaffRoster(Store, calendar);
            var notifier = new TaskNotifier(Store, Clock);
            dispatch = new TaskDispatch(Store, roster, notifier, new TaskVerification(Store, Clock, notifier), Clock, schema);
            views = new TaskViews(Store);
        }

        public Guid User(string role, string name)
        {
            var user = new UserAccount { UserId = Guid.NewGuid(), RoleId = Store.Values.OfType<Role>().Single(r => r.Name == role).RoleId, FullName = name, Email = $"{name}@test", Status = "ACTIVE",
                SpecialtyId = role == "STAFF" ? Store.Values.OfType<StaffSpecialty>().Single().SpecialtyId : null };
            Store.Values.Add(user);
            return user.UserId;
        }

        public Guid Staff(string name, bool checkedIn = true)
        {
            var id = User("STAFF", name);
            var assignment = Assignment(id, ShiftAssignmentStatus.Scheduled, new(2026, 10, 11));
            if (checkedIn) Store.Values.OfType<ShiftAssignment>().Single(s => s.ShiftAssignmentId == assignment).CheckInAt = Now.AddHours(-2);
            return id;
        }

        public void Setting(string key, string value) =>
            Store.Values.Add(new SystemSetting { SettingId = Guid.NewGuid(), SettingKey = key, SettingValue = value, ValueType = "INTEGER", UpdatedAt = Now });

        public Task<int> Tick() => dispatch.Tick(default);
        public List<Notification> Notifications(WorkTask task, string type) => Store.Values.OfType<Notification>().Where(n => n.TaskId == task.TaskId && n.NotificationType == type).ToList();
        public Guid[] Recipients(Notification notification) => Store.Values.OfType<NotificationRecipient>().Where(r => r.NotificationId == notification.NotificationId).Select(r => r.UserId).ToArray();

        public Guid Assignment(Guid user, string status, DateOnly day)
        {
            var assignment = new ShiftAssignment { ShiftAssignmentId = Guid.NewGuid(), UserId = user, ShiftId = shiftId, WorkDate = day, Status = status };
            Store.Values.Add(assignment);
            Store.Values.Add(new StaffZoneAssignment { StaffZoneAssignmentId = Guid.NewGuid(), ShiftAssignmentId = assignment.ShiftAssignmentId, ZoneId = zone.ZoneId, AssignedAt = Now.AddHours(-3) });
            return assignment.ShiftAssignmentId;
        }

        public void Policy(int offerTimeoutSec = 60)
        {
            var policy = new SlaPolicy { PolicyId = Guid.NewGuid(), EventTypeId = eventTypeId, ProblemDomain = "OPERATIONAL_EVENT",
                IsEnabled = true, Version = 1, PriorityCode = "MEDIUM", ResponseTargetSec = 300, ResolutionTargetSec = 3600,
                OfferTimeoutSec = offerTimeoutSec, WarningPercent = 80 };
            Store.Values.Add(policy);
            Store.Values.Add(new SlaPolicyAction { ActionId = Guid.NewGuid(), PolicyId = policy.PolicyId, ActionType = "CREATE_OPERATIONAL_TASK", ExecutionMode = "AUTOMATIC",
                ConditionJson = """{"alert_level":"CRITICAL"}""", IsEnabled = true,
                ActionParametersJson = """{"title":"Clear the queue","instructions":"Open one more counter","taskTypeCode":"TT-01"}""" });
        }

        public async Task Raise()
        {
            var ev = new OperationalEvent { EventId = Guid.NewGuid(), CameraId = camera.CameraId, ZoneId = zone.ZoneId, RuleId = ruleId, EventTypeId = eventTypeId,
                Priority = "CRITICAL", MetricValue = 7, DetectedAt = Clock.UtcNow, LastObservedAt = Clock.UtcNow, Status = "ACTIVE",
                MetadataJson = """{"measurementMode":"QUEUE_LENGTH","thresholdUnit":"PEOPLE"}""" };
            Store.Values.Add(ev);
            await dispatch.Raised(ev, null, default);
        }

        public TaskOffer OfferFor(Guid staff) => Store.Values.OfType<TaskOffer>().Where(a => a.StaffUserId == staff)
            .OrderByDescending(a => a.OfferedAt).ThenByDescending(a => a.OfferId).First();

        public StaffTasks StaffTasks(Guid user) => new(Store, new User(user, "STAFF"), Clock, calendar, roster, dispatch, views, schema);
    }

    private sealed record User(Guid UserId, string Role) : ICurrentUser;
    private sealed class Schema : ITaskSchema { public Task<bool> Ready(CancellationToken ct) => Task.FromResult(true); }
}
