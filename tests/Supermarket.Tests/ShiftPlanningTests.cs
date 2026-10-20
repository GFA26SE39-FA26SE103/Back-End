using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
using AppError = Supermarket.Application.ApplicationException;
namespace Supermarket.Tests;

public sealed class ShiftPlanningTests
{
    // Monday 19/10/2026 10:00 store time (UTC+7).
    private static readonly DateTime Now = new(2026, 10, 19, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Wed = new(2026, 10, 21), Thu = new(2026, 10, 22);

    [Fact]
    public async Task TemplatesMustBe4To8HoursAndKeepTheirTimesWhileInUse()
    {
        var w = new World(); var manager = w.User("MANAGER", "Lan");
        var plan = w.Planning(manager);

        Assert.Equal("INVALID_SHIFT_LENGTH", (await Assert.ThrowsAsync<DomainException>(() => plan.CreateTemplate(new("Odd", new(6, 0), new(11, 0)), default))).Code);
        var night = await plan.CreateTemplate(new("Night", new(22, 0), new(6, 0)), default);
        Assert.Equal((8.0, ShiftTemplateStatus.Active, false), (night.Hours, night.Status, night.InUse));
        Assert.Equal("DUPLICATE", (await Assert.ThrowsAsync<AppError>(() => plan.CreateTemplate(new("Night", new(18, 0), new(22, 0)), default))).Code);

        var an = w.Staff("An");
        await plan.Assign(new(an, w.Morning.ShiftId, Wed, [w.Checkout.ZoneId]), default);
        Assert.Equal("SHIFT_IN_USE", (await Assert.ThrowsAsync<AppError>(() => plan.UpdateTemplate(w.Morning.ShiftId, new(null, new(7, 0), new(15, 0), null), default))).Code);
        var renamed = await plan.UpdateTemplate(w.Morning.ShiftId, new("Morning A", null, null, ShiftTemplateStatus.Inactive), default);
        Assert.Equal(("Morning A", ShiftTemplateStatus.Inactive, true), (renamed.Name, renamed.Status, renamed.InUse));
        Assert.Equal("SHIFT_INACTIVE", (await Assert.ThrowsAsync<AppError>(() => plan.Assign(new(w.Staff("Binh"), w.Morning.ShiftId, Thu, [w.Checkout.ZoneId]), default))).Code);
    }

    [Fact]
    public async Task DraftsStayHiddenUntilPublishAndPublishIsBlockedWhileAZoneIsShort()
    {
        var w = new World(); var manager = w.User("MANAGER", "Lan"); var plan = w.Planning(manager);
        var an = w.Staff("An"); var binh = w.Staff("Binh");
        var morning = await plan.Assign(new(an, w.Morning.ShiftId, Wed, [w.Checkout.ZoneId]), default);

        Assert.Equal(ShiftAssignmentStatus.Draft, morning.Status);
        Assert.Empty(await w.StaffTasks(an).Shifts(Wed, Wed, default));
        var schedule = await plan.Schedule(Wed, Wed, default);
        Assert.Equal((1, 1), (schedule.DraftCount, schedule.GapCount));
        var gap = Assert.Single(schedule.Coverage, c => c.BelowMinimum);
        Assert.Equal((w.Evening.ShiftId, 0, 1), (gap.ShiftId, gap.Planned, gap.MinStaff));

        var blocked = await Assert.ThrowsAsync<AppError>(() => plan.Publish(new(Wed, Wed), default));
        Assert.Equal("COVERAGE_GAPS", blocked.Code); Assert.Contains("Evening", blocked.Message);
        Assert.Equal(ShiftAssignmentStatus.Draft, (await plan.Schedule(Wed, Wed, default)).Assignments.Single().Status);

        await plan.Assign(new(binh, w.Evening.ShiftId, Wed, [w.Checkout.ZoneId]), default);
        var result = await plan.Publish(new(Wed, Wed), default);

        Assert.Equal((2, 2), (result.Published, result.Notified));
        var mine = Assert.Single(await w.StaffTasks(an).Shifts(Wed, Wed, default));
        Assert.Equal((ShiftAssignmentStatus.Scheduled, w.Checkout.ZoneId), (mine.Status, mine.Zones.Single().ZoneId));
        Assert.Equal(new[] { an, binh }.Order(), w.Recipients(ShiftPlanning.Published).Order());
        Assert.Equal(0, (await plan.Schedule(Wed, Wed, default)).GapCount);
    }

    [Fact]
    public async Task AssignmentRespectsRoleOverlapLeaveWeeklyLimitAndStartedShifts()
    {
        var w = new World(); var manager = w.User("MANAGER", "Lan"); var plan = w.Planning(manager);
        var an = w.Staff("An"); var zone = new[] { w.Checkout.ZoneId };

        Assert.Equal("NOT_STAFF", (await Assert.ThrowsAsync<AppError>(() => plan.Assign(new(manager, w.Morning.ShiftId, Wed, zone), default))).Code);
        Assert.Equal("ZONE_REQUIRED", (await Assert.ThrowsAsync<DomainException>(() => plan.Assign(new(an, w.Morning.ShiftId, Wed, []), default))).Code);
        Assert.Equal("ZONE_NOT_FOUND", (await Assert.ThrowsAsync<AppError>(() => plan.Assign(new(an, w.Morning.ShiftId, Wed, [Guid.NewGuid()]), default))).Code);
        Assert.Equal("SHIFT_LOCKED", (await Assert.ThrowsAsync<AppError>(() => plan.Assign(new(an, w.Morning.ShiftId, new(2026, 10, 19), zone), default))).Code);

        await plan.Assign(new(an, w.Morning.ShiftId, Wed, zone), default);
        Assert.Equal("ALREADY_ASSIGNED", (await Assert.ThrowsAsync<AppError>(() => plan.Assign(new(an, w.Morning.ShiftId, Wed, zone), default))).Code);
        w.Store.Values.Add(w.Mid);
        var overlap = await Assert.ThrowsAsync<AppError>(() => plan.Assign(new(an, w.Mid.ShiftId, Wed, zone), default));
        Assert.Equal("SCHEDULE_CONFLICT", overlap.Code); Assert.Contains("overlapping", overlap.Message);

        var binh = w.Staff("Binh");
        await plan.AddUnavailability(new(binh, w.Local(Thu, 0), w.Local(Thu, 23), "Exam"), default);
        var leave = await Assert.ThrowsAsync<AppError>(() => plan.Assign(new(binh, w.Morning.ShiftId, Thu, zone), default));
        Assert.Contains("unavailable", leave.Message);

        var chi = w.Staff("Chi");
        foreach (var day in Enumerable.Range(20, 6)) await plan.Assign(new(chi, w.Morning.ShiftId, new DateOnly(2026, 10, day), zone), default);
        var hours = await Assert.ThrowsAsync<AppError>(() => plan.Assign(new(chi, w.Evening.ShiftId, new DateOnly(2026, 10, 25), zone), default));
        Assert.Contains("56/48", hours.Message);

        var options = await plan.StaffOptions(w.Morning.ShiftId, Thu, null, default);
        Assert.Equal(new[] { an }, options.Where(o => o.Eligible).Select(o => o.UserId));
        Assert.Contains(options, o => o.UserId == binh && o.Problems.Any(p => p.Contains("unavailable")));
        Assert.Contains(options, o => o.UserId == chi && o.Problems.Any(p => p.Contains("already on this shift")));
    }

    [Fact]
    public async Task CancelledRowIsReusedAndCancellingAPublishedShiftCancelsItsPendingSwap()
    {
        var w = new World(); var manager = w.User("MANAGER", "Lan"); var plan = w.Planning(manager);
        var an = w.Staff("An"); var binh = w.Staff("Binh");
        var a = await plan.Assign(new(an, w.Morning.ShiftId, Thu, [w.Checkout.ZoneId]), default);
        await plan.Assign(new(binh, w.Evening.ShiftId, Thu, [w.Checkout.ZoneId]), default);
        await plan.Publish(new(Thu, Thu), default);
        var swap = await w.Swaps(an).Create(new(a.ShiftAssignmentId, SwapType.Cover, binh, null, null, null), default);

        Assert.Equal("SWAP_PENDING", (await Assert.ThrowsAsync<AppError>(() => plan.Update(a.ShiftAssignmentId, new(binh, null), default))).Code);
        var cancelled = await plan.Cancel(a.ShiftAssignmentId, new("Store closed early"), default);

        Assert.Equal(ShiftAssignmentStatus.Cancelled, cancelled.Status);
        Assert.Equal(SwapStatus.Cancelled, (await w.Swaps(an).Get(swap.SwapRequestId, default)).Status);
        Assert.Contains(an, w.Recipients(ShiftPlanning.Removed));
        Assert.DoesNotContain(w.Store.Values.OfType<StaffZoneAssignment>(), z => z.ShiftAssignmentId == a.ShiftAssignmentId && z.UnassignedAt == null);
        Assert.DoesNotContain(await w.StaffTasks(an).Shifts(Thu, Thu, default), s => s.Status != ShiftAssignmentStatus.Cancelled);

        var again = await plan.Assign(new(an, w.Morning.ShiftId, Thu, [w.Checkout.ZoneId]), default);
        Assert.Equal((a.ShiftAssignmentId, ShiftAssignmentStatus.Draft), (again.ShiftAssignmentId, again.Status));
    }

    [Fact]
    public async Task ReassigningAPublishedShiftNotifiesBothStaffAndZonesCanBeReplaced()
    {
        var w = new World(); var manager = w.User("MANAGER", "Lan"); var plan = w.Planning(manager);
        var an = w.Staff("An"); var binh = w.Staff("Binh"); var chi = w.Staff("Chi");
        var a = await plan.Assign(new(an, w.Morning.ShiftId, Thu, [w.Checkout.ZoneId]), default);
        await plan.Assign(new(binh, w.Evening.ShiftId, Thu, [w.Checkout.ZoneId]), default);
        await plan.Publish(new(Thu, Thu), default);

        var moved = await plan.Update(a.ShiftAssignmentId, new(chi, [w.Checkout.ZoneId, w.Produce.ZoneId]), default);

        Assert.Equal(chi, moved.UserId);
        Assert.Equal(new[] { w.Checkout.ZoneId, w.Produce.ZoneId }.Order(), moved.Zones.Select(z => z.ZoneId).Order());
        Assert.Equal(new[] { an }, w.Recipients(ShiftPlanning.Removed));
        Assert.Equal(new[] { chi }, w.Recipients(ShiftPlanning.Changed));
        Assert.Empty(await w.StaffTasks(an).Shifts(Thu, Thu, default));
        Assert.Single(await w.StaffTasks(chi).Shifts(Thu, Thu, default));
    }

    [Fact]
    public async Task LeaveCannotOverlapExistingShiftsAndCanBeRemoved()
    {
        var w = new World(); var manager = w.User("MANAGER", "Lan"); var plan = w.Planning(manager);
        var an = w.Staff("An");
        await plan.Assign(new(an, w.Morning.ShiftId, Wed, [w.Checkout.ZoneId]), default);

        var clash = await Assert.ThrowsAsync<AppError>(() => plan.AddUnavailability(new(an, w.Local(Wed, 0), w.Local(Wed, 12), null), default));
        Assert.Equal("LEAVE_CONFLICTS_SCHEDULE", clash.Code);
        var leave = await plan.AddUnavailability(new(an, w.Local(Thu, 0), w.Local(Thu, 23), "Family"), default);
        Assert.Equal(("An", "Family"), (leave.FullName, leave.Note));
        Assert.Single(await plan.Unavailability(Thu, Thu, an, default));

        await plan.RemoveUnavailability(leave.UnavailabilityId, default);
        Assert.Empty(await plan.Unavailability(Thu, Thu, an, default));
        await plan.Assign(new(an, w.Morning.ShiftId, Thu, [w.Checkout.ZoneId]), default);
    }

    [Fact]
    public async Task OnlyTheManagerPlansWhileOperationalRolesCanReadTheSchedule()
    {
        var w = new World(); var an = w.Staff("An"); var op = w.User("OPERATOR", "Op"); var admin = w.User("ADMIN", "Ad");
        Assert.Equal(403, (await Assert.ThrowsAsync<AppError>(() => w.Planning(op).Assign(new(an, w.Morning.ShiftId, Wed, [w.Checkout.ZoneId]), default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<AppError>(() => w.Planning(admin).Publish(new(Wed, Wed), default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<AppError>(() => w.Planning(an).Schedule(null, null, default))).Status);
        var week = await w.Planning(op).Schedule(null, null, default);
        Assert.Equal((new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 25)), (week.From, week.To));
        Assert.Equal(2, (await w.Planning(admin).Templates(default)).Length);
    }

    private sealed class World
    {
        public MonitoringSetupTests.MemoryStore Store { get; } = new();
        public StaffTaskTests.Clock Clock { get; } = new() { UtcNow = Now };
        public StoreCalendar Calendar { get; } = new(StoreCalendar.Zone(null));
        public Shift Morning { get; } = new() { ShiftId = Guid.NewGuid(), Name = "Morning", StartTime = new(6, 0), EndTime = new(14, 0), Status = "ACTIVE" };
        public Shift Evening { get; } = new() { ShiftId = Guid.NewGuid(), Name = "Evening", StartTime = new(14, 0), EndTime = new(22, 0), Status = "ACTIVE" };
        public Shift Mid { get; } = new() { ShiftId = Guid.NewGuid(), Name = "Mid", StartTime = new(10, 0), EndTime = new(16, 0), Status = "ACTIVE" };
        public Zone Checkout { get; } = new() { ZoneId = Guid.NewGuid(), Code = "B", Name = "Zone B · Checkout", MinStaff = 1 };
        public Zone Produce { get; } = new() { ZoneId = Guid.NewGuid(), Code = "C", Name = "Zone C · Produce", MinStaff = 0 };
        private readonly SwapPlanner planner;
        private readonly TaskNotifier notifier;

        public World()
        {
            foreach (var role in new[] { "ADMIN", "OPERATOR", "MANAGER", "STAFF" }) Store.Values.Add(new Role { RoleId = Guid.NewGuid(), Name = role });
            Store.Values.AddRange([new Supermarket.Domain.Supermarket { SupermarketId = Guid.NewGuid(), Name = "Central" }, Morning, Evening, Checkout, Produce]);
            planner = new SwapPlanner(Store, Calendar);
            notifier = new TaskNotifier(Store, Clock);
        }
        public DateTime Local(DateOnly day, int hour) => new DateTime(day.Year, day.Month, day.Day, hour, 0, 0, DateTimeKind.Utc).AddHours(-7);
        public Guid User(string role, string name)
        {
            var user = new UserAccount { UserId = Guid.NewGuid(), RoleId = Store.Values.OfType<Role>().Single(r => r.Name == role).RoleId, FullName = name, Email = $"{name}@test", Status = "ACTIVE" };
            Store.Values.Add(user);
            return user.UserId;
        }
        public Guid Staff(string name) => User("STAFF", name);
        public Guid[] Recipients(string type)
        {
            var ids = Store.Values.OfType<Notification>().Where(n => n.NotificationType == type).Select(n => n.NotificationId).ToHashSet();
            return Store.Values.OfType<NotificationRecipient>().Where(r => ids.Contains(r.NotificationId)).Select(r => r.UserId).Distinct().ToArray();
        }
        private Current Me(Guid user)
        {
            var account = Store.Values.OfType<UserAccount>().Single(u => u.UserId == user);
            return new(user, Store.Values.OfType<Role>().Single(r => r.RoleId == account.RoleId).Name);
        }
        public ShiftPlanning Planning(Guid user) => new(Store, Me(user), Clock, Calendar, planner, notifier, new SwapSchema());
        public ShiftSwaps Swaps(Guid user) => new(Store, Me(user), Clock, Calendar, planner, notifier, new SwapSchema());
        public StaffTasks StaffTasks(Guid user)
        {
            var roster = new StaffRoster(Store, Calendar);
            var dispatch = new TaskDispatch(Store, roster, notifier, Clock, new TaskSchema());
            return new(Store, Me(user), Clock, Calendar, roster, dispatch, new TaskViews(Store, dispatch), new TaskSchema());
        }
    }
    private sealed record Current(Guid UserId, string Role) : ICurrentUser;
    private sealed class SwapSchema : IShiftSwapSchema { public Task<bool> Ready(CancellationToken ct) => Task.FromResult(true); }
    private sealed class TaskSchema : ITaskSchema { public Task<bool> Ready(CancellationToken ct) => Task.FromResult(true); }
}
