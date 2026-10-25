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

        var first = (await staff.Respond(offer.TaskAssignmentId, new(TaskResponse.HandleNow, null, null), default)).Task!;
        var retry = (await staff.Respond(offer.TaskAssignmentId, new(TaskResponse.HandleNow, null, null), default)).Task!;

        Assert.Equal((first.TaskId, WorkTaskStatus.InProgress, a), (retry.TaskId, retry.Status, retry.AssigneeUserId));
        Assert.Equal(first.StartedAt, retry.StartedAt);
        // Declining a task I already hold is not a retry.
        Assert.Equal("OFFER_CLOSED", (await Assert.ThrowsAsync<AppError>(() => staff.Respond(offer.TaskAssignmentId, new(TaskResponse.CannotHandle, null, "Busy"), default))).Code);
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
        var task = (await w.StaffTasks(a).Respond(w.OfferFor(a).TaskAssignmentId, new(TaskResponse.HandleLater, Now.AddMinutes(10), null), default)).Task!;

        Assert.Equal("NOT_FOUND", (await Assert.ThrowsAsync<AppError>(() => w.StaffTasks(b).Start(task.TaskId, default))).Code);
        Assert.Empty(await w.StaffTasks(b).Tasks(null, default));
        Assert.Equal("INVALID_STATUS", (await Assert.ThrowsAsync<DomainException>(() => w.StaffTasks(a).Tasks("DONE", default))).Code);
        Assert.Equal(WorkTaskStatus.InProgress, (await w.StaffTasks(a).Start(task.TaskId, default)).Status);
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
            Store.Values.AddRange([zone, camera,
                new OperationalEventType { EventTypeId = eventTypeId, Code = "LONG_QUEUE", Name = "Long queue", MeasurementType = "QUEUE_LENGTH" },
                new Shift { ShiftId = shiftId, Name = "Morning", StartTime = new(8, 0), EndTime = new(16, 0) }]);
            roster = new StaffRoster(Store, calendar);
            dispatch = new TaskDispatch(Store, roster, new TaskNotifier(Store, Clock), Clock, schema);
            views = new TaskViews(Store, dispatch);
        }

        public Guid Staff(string name, bool checkedIn = true)
        {
            var user = new UserAccount { UserId = Guid.NewGuid(), RoleId = Store.Values.OfType<Role>().Single(r => r.Name == "STAFF").RoleId, FullName = name, Email = $"{name}@test", Status = "ACTIVE" };
            Store.Values.Add(user);
            var assignment = Assignment(user.UserId, ShiftAssignmentStatus.Scheduled, new(2026, 10, 11));
            if (checkedIn) Store.Values.OfType<ShiftAssignment>().Single(s => s.ShiftAssignmentId == assignment).CheckInAt = Now.AddHours(-2);
            return user.UserId;
        }

        public Guid Assignment(Guid user, string status, DateOnly day)
        {
            var assignment = new ShiftAssignment { ShiftAssignmentId = Guid.NewGuid(), UserId = user, ShiftId = shiftId, WorkDate = day, Status = status };
            Store.Values.Add(assignment);
            Store.Values.Add(new StaffZoneAssignment { StaffZoneAssignmentId = Guid.NewGuid(), ShiftAssignmentId = assignment.ShiftAssignmentId, ZoneId = zone.ZoneId, AssignedAt = Now.AddHours(-3) });
            return assignment.ShiftAssignmentId;
        }

        public void Policy()
        {
            var policy = new ResponsePolicy { PolicyId = Guid.NewGuid(), RuleId = ruleId, EventPriority = "WARNING" };
            Store.Values.Add(policy);
            Store.Values.Add(new ResponsePolicyAction { ActionId = Guid.NewGuid(), PolicyId = policy.PolicyId, ActionType = "CREATE_OPERATIONAL_TASK", ExecutionMode = "AUTOMATIC",
                ActionParametersJson = """{"title":"Clear the queue","instructions":"Open one more counter"}""" });
        }

        public async Task Raise()
        {
            var ev = new OperationalEvent { EventId = Guid.NewGuid(), CameraId = camera.CameraId, ZoneId = zone.ZoneId, RuleId = ruleId, EventTypeId = eventTypeId,
                Priority = "WARNING", MetricValue = 7, DetectedAt = Clock.UtcNow, LastObservedAt = Clock.UtcNow, Status = "ACTIVE",
                MetadataJson = """{"measurementMode":"QUEUE_LENGTH","thresholdUnit":"PEOPLE"}""" };
            Store.Values.Add(ev);
            await dispatch.Raised(ev, null, default);
        }

        public TaskAssignmentHistory OfferFor(Guid staff) => Store.Values.OfType<TaskAssignmentHistory>().Single(a => a.StaffUserId == staff && a.AssignmentType == TaskAssignmentType.Offer);

        public StaffTasks StaffTasks(Guid user) => new(Store, new User(user, "STAFF"), Clock, calendar, roster, dispatch, views, schema);
    }

    private sealed record User(Guid UserId, string Role) : ICurrentUser;
    private sealed class Schema : ITaskSchema { public Task<bool> Ready(CancellationToken ct) => Task.FromResult(true); }
}
