using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
using AppError = Supermarket.Application.ApplicationException;
namespace Supermarket.Tests;

public sealed class ShiftSwapTests
{
    // Monday 19/10/2026 10:00 store time (UTC+7). Morning 06-14, Evening 14-22 local.
    private static readonly DateTime Now = new(2026, 10, 19, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Fri = new(2026, 10, 23), Sat = new(2026, 10, 24);

    [Fact]
    public async Task CoverCandidatesHideColleaguesWhoOverlapAreUnavailableOrOverTheWeeklyLimit()
    {
        var w = new World();
        var an = w.Staff("An"); var mine = w.Assign(an, w.Evening, Fri);
        var binh = w.Staff("Binh"); w.Assign(binh, w.Morning, Sat);
        var chi = w.Staff("Chi"); w.Assign(chi, w.Evening, Fri);
        var dung = w.Staff("Dung"); w.Unavailable(dung, Fri, 12, 23);
        var em = w.Staff("Em"); foreach (var day in Enumerable.Range(19, 6)) w.Assign(em, w.Morning, new DateOnly(2026, 10, day));

        var result = await w.Swaps(an).Candidates(mine.ShiftAssignmentId, SwapType.Cover, default);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal(binh, candidate.Colleague.UserId);
        Assert.Null(candidate.Shift);
        Assert.Equal(16, candidate.WeeklyHoursAfter);
        Assert.True(candidate.Recommended);
        Assert.Equal(3, result.HiddenCount);
        Assert.Equal(w.Local(Fri, 14) - TimeSpan.FromHours(24), result.Deadline);
        _ = chi; _ = em;
    }

    [Fact]
    public async Task SwapCandidatesListTheColleaguesShiftsThatCanBeExchanged()
    {
        var w = new World();
        var an = w.Staff("An"); var mine = w.Assign(an, w.Evening, Fri);
        var binh = w.Staff("Binh"); var sat = w.Assign(binh, w.Morning, Sat);
        var chi = w.Staff("Chi"); w.Assign(chi, w.Evening, Fri);

        var result = await w.Swaps(an).Candidates(mine.ShiftAssignmentId, SwapType.Swap, default);

        // Chi's Friday evening is the same shift, so there is nothing to exchange with Chi.
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal((binh, sat.ShiftAssignmentId), (candidate.Colleague.UserId, candidate.Shift!.ShiftAssignmentId));
        Assert.Equal(1, result.HiddenCount);
    }

    [Fact]
    public async Task SwapRequestGoesToTheColleagueAndExpires24HoursBeforeTheEarliestShift()
    {
        var w = new World();
        var an = w.Staff("An"); var mine = w.Assign(an, w.Evening, Fri);
        var binh = w.Staff("Binh"); var theirs = w.Assign(binh, w.Morning, Sat);

        var view = await w.Swaps(an).Create(new(mine.ShiftAssignmentId, SwapType.Swap, binh, theirs.ShiftAssignmentId, SwapReason.Study, " Exam "), default);

        Assert.Equal((SwapStatus.PendingPeer, "REQUESTER", "Exam"), (view.Status, view.ViewerRole, view.Note));
        Assert.Equal(w.Local(Fri, 14) - TimeSpan.FromHours(24), view.ExpiresAt);
        Assert.True(view.CanCancel); Assert.False(view.CanRespond);
        Assert.Empty(view.Checks);
        Assert.Equal(new[] { binh }, w.Recipients(ShiftSwaps.Received));
        var received = Assert.Single(await w.Swaps(binh).Mine("received", default));
        Assert.True(received.CanRespond); Assert.Equal("TARGET", received.ViewerRole);
        Assert.Empty(await w.Swaps(binh).Mine("sent", default));
    }

    [Fact]
    public async Task AcceptedSwapNeedsManagerApprovalAndThenExchangesBothShifts()
    {
        var w = new World();
        var an = w.Staff("An"); var mine = w.Assign(an, w.Evening, Fri);
        var binh = w.Staff("Binh"); var theirs = w.Assign(binh, w.Morning, Sat);
        var manager = w.User("MANAGER", "Lan");
        var id = (await w.Swaps(an).Create(new(mine.ShiftAssignmentId, SwapType.Swap, binh, theirs.ShiftAssignmentId, null, null), default)).SwapRequestId;

        var accepted = await w.Swaps(binh).Respond(id, new(true), default);
        Assert.Equal(SwapStatus.PendingManager, accepted.Status);
        Assert.Equal(Now, accepted.TargetRespondedAt);
        Assert.Equal((an, binh), (mine.UserId, theirs.UserId));
        Assert.Equal(new[] { an }, w.Recipients(ShiftSwaps.PeerAccepted));
        Assert.Equal(new[] { manager }, w.Recipients(ShiftSwaps.PendingApproval));
        var queued = Assert.Single(await w.Swaps(manager).ForManager(null, default));
        Assert.True(queued.CanDecide);

        var approved = await w.Swaps(manager).Approve(id, new("OK"), default);

        Assert.Equal((SwapStatus.Approved, manager, "OK"), (approved.Status, approved.DecidedBy!.UserId, approved.DecisionNote));
        Assert.Equal((binh, an), (mine.UserId, theirs.UserId));
        Assert.Equal(new[] { an, binh }.Order(), w.Recipients(ShiftSwaps.Approved).Order());
        Assert.Empty(await w.Swaps(manager).ForManager(null, default));
    }

    [Fact]
    public async Task ApprovedCoverMovesTheRequestersShiftToTheColleague()
    {
        var w = new World();
        var an = w.Staff("An"); var mine = w.Assign(an, w.Evening, Fri);
        var binh = w.Staff("Binh"); var manager = w.User("MANAGER", "Lan");
        var id = (await w.Swaps(an).Create(new(mine.ShiftAssignmentId, SwapType.Cover, binh, null, SwapReason.Health, null), default)).SwapRequestId;
        await w.Swaps(binh).Respond(id, new(true), default);

        await w.Swaps(manager).Approve(id, new(null), default);

        Assert.Equal(binh, mine.UserId);
        Assert.Equal(SwapStatus.Approved, (await w.Swaps(an).Get(id, default)).Status);
        Assert.Empty(await w.Swaps(an).Mine("received", default));
    }

    [Fact]
    public async Task StaffCanAskManagerForHelpAndManagerAssignsCoverWithoutPeerAcceptance()
    {
        var w = new World();
        var an = w.Staff("An"); var mine = w.Assign(an, w.Evening, Fri);
        var binh = w.Staff("Binh"); var manager = w.User("MANAGER", "Lan");

        var help = await w.Swaps(an).RequestManagerHelp(new(mine.ShiftAssignmentId, SwapReason.Health, "Need help"), default);

        Assert.Equal(SwapStatus.PendingManager, help.Status);
        Assert.Null(help.Target);
        Assert.Equal(new[] { manager }, w.Recipients(ShiftSwaps.ManagerHelp));
        Assert.Equal(help.SwapRequestId, Assert.Single(await w.Swaps(manager).ForManager(null, default)).SwapRequestId);
        var candidate = Assert.Single((await w.Swaps(manager).ManagerCandidates(help.SwapRequestId, default)).Candidates);
        Assert.Equal(binh, candidate.Colleague.UserId);

        var assigned = await w.Swaps(manager).AssignCover(help.SwapRequestId, new(binh, "Manager coverage decision"), default);

        Assert.Equal(SwapStatus.Approved, assigned.Status);
        Assert.Equal(binh, mine.UserId);
        Assert.Equal(manager, assigned.DecidedBy!.UserId);
        Assert.Equal(new[] { an, binh }.Order(), w.Recipients(ShiftSwaps.Approved).Order());
    }

    [Fact]
    public async Task ManagerCanDirectlyCoverOrSwapUntilTheShiftStarts()
    {
        var w = new World();
        var manager = w.User("MANAGER", "Lan");
        var an = w.Staff("An"); var urgent = w.Assign(an, w.Morning, new(2026, 10, 20));
        var binh = w.Staff("Binh");

        var covered = await w.Swaps(manager).Direct(new(SwapType.Cover, urgent.ShiftAssignmentId, binh, null, "Urgent absence"), default);
        Assert.Equal((SwapStatus.Approved, binh), (covered.Status, urgent.UserId));

        var chi = w.Staff("Chi"); var first = w.Assign(chi, w.Evening, Fri);
        var dung = w.Staff("Dung"); var second = w.Assign(dung, w.Morning, Sat);
        var swapped = await w.Swaps(manager).Direct(new(SwapType.Swap, first.ShiftAssignmentId, dung, second.ShiftAssignmentId, "Balance coverage"), default);

        Assert.Equal(SwapStatus.Approved, swapped.Status);
        Assert.Equal((dung, chi), (first.UserId, second.UserId));
        Assert.Equal("Balance coverage", swapped.DecisionNote);
    }

    [Fact]
    public async Task ApprovalRechecksTheRulesAndLeavesTheScheduleUnchangedWhenOneFails()
    {
        var w = new World();
        var an = w.Staff("An"); var mine = w.Assign(an, w.Evening, Fri);
        var binh = w.Staff("Binh"); var theirs = w.Assign(binh, w.Morning, Sat);
        var manager = w.User("MANAGER", "Lan");
        var id = (await w.Swaps(an).Create(new(mine.ShiftAssignmentId, SwapType.Swap, binh, theirs.ShiftAssignmentId, null, null), default)).SwapRequestId;
        await w.Swaps(binh).Respond(id, new(true), default);
        w.Unavailable(an, Sat, 5, 12);

        var detail = await w.Swaps(manager).Get(id, default);
        Assert.Contains(detail.Checks, c => c.Code == "AVAILABLE" && !c.Passed);
        var error = await Assert.ThrowsAsync<AppError>(() => w.Swaps(manager).Approve(id, new(null), default));

        Assert.Equal("SWAP_NOT_ALLOWED", error.Code);
        Assert.Contains("An is not marked unavailable", error.Message);
        Assert.Equal((an, binh), (mine.UserId, theirs.UserId));
        Assert.Equal(SwapStatus.PendingManager, (await w.Swaps(manager).Get(id, default)).Status);
    }

    [Fact]
    public async Task RequestsAfterTheDeadlineOrForAShiftWithAnOpenRequestAreRefused()
    {
        var w = new World();
        var an = w.Staff("An"); var tomorrow = w.Assign(an, w.Morning, new(2026, 10, 20)); var mine = w.Assign(an, w.Evening, Fri);
        var binh = w.Staff("Binh"); var chi = w.Staff("Chi");

        var late = await Assert.ThrowsAsync<AppError>(() => w.Swaps(an).Create(new(tomorrow.ShiftAssignmentId, SwapType.Cover, binh, null, null, null), default));
        Assert.Equal("SWAP_NOT_ALLOWED", late.Code); Assert.Contains("24 hours before", late.Message);

        await w.Swaps(an).Create(new(mine.ShiftAssignmentId, SwapType.Cover, binh, null, null, null), default);
        var twice = await Assert.ThrowsAsync<AppError>(() => w.Swaps(an).Create(new(mine.ShiftAssignmentId, SwapType.Cover, chi, null, null, null), default));
        Assert.Contains("only one pending", twice.Message);
    }

    [Fact]
    public async Task RequesterCancelsWhilePendingAndColleagueCanOnlyWithdrawAfterAccepting()
    {
        var w = new World();
        var an = w.Staff("An"); var mine = w.Assign(an, w.Evening, Fri); var other = w.Assign(an, w.Morning, Sat);
        var binh = w.Staff("Binh"); var manager = w.User("MANAGER", "Lan");
        var first = (await w.Swaps(an).Create(new(mine.ShiftAssignmentId, SwapType.Cover, binh, null, null, null), default)).SwapRequestId;

        var notYet = await Assert.ThrowsAsync<AppError>(() => w.Swaps(binh).Cancel(first, new(null), default));
        Assert.Equal("SWAP_NOT_PENDING", notYet.Code);
        var cancelled = await w.Swaps(an).Cancel(first, new("Plans changed"), default);
        Assert.Equal((SwapStatus.Cancelled, an, "Plans changed"), (cancelled.Status, cancelled.CancelledBy!.UserId, cancelled.CancellationReason));
        Assert.Equal(new[] { binh }, w.Recipients(ShiftSwaps.Cancelled));

        var second = (await w.Swaps(an).Create(new(other.ShiftAssignmentId, SwapType.Cover, binh, null, null, null), default)).SwapRequestId;
        await w.Swaps(binh).Respond(second, new(true), default);
        var withdrawn = await w.Swaps(binh).Cancel(second, new(null), default);
        Assert.Equal((SwapStatus.Cancelled, binh), (withdrawn.Status, withdrawn.CancelledBy!.UserId));
        Assert.Contains(manager, w.Recipients(ShiftSwaps.Cancelled));
        Assert.Equal(an, other.UserId);
    }

    [Fact]
    public async Task DeclinedRequestEndsWithoutReachingTheManager()
    {
        var w = new World();
        var an = w.Staff("An"); var mine = w.Assign(an, w.Evening, Fri);
        var binh = w.Staff("Binh"); var manager = w.User("MANAGER", "Lan");
        var id = (await w.Swaps(an).Create(new(mine.ShiftAssignmentId, SwapType.Cover, binh, null, null, null), default)).SwapRequestId;

        var declined = await w.Swaps(binh).Respond(id, new(false), default);

        Assert.Equal(SwapStatus.DeclinedByPeer, declined.Status);
        Assert.Equal(new[] { an }, w.Recipients(ShiftSwaps.PeerDeclined));
        Assert.Empty(await w.Swaps(manager).ForManager(null, default));
        Assert.Equal("SWAP_NOT_PENDING", (await Assert.ThrowsAsync<AppError>(() => w.Swaps(binh).Respond(id, new(true), default))).Code);
    }

    [Fact]
    public async Task PendingRequestsExpireAtTheDeadlineAndCanNoLongerChange()
    {
        var w = new World();
        var an = w.Staff("An"); var mine = w.Assign(an, w.Evening, Fri);
        var binh = w.Staff("Binh"); var manager = w.User("MANAGER", "Lan");
        var id = (await w.Swaps(an).Create(new(mine.ShiftAssignmentId, SwapType.Cover, binh, null, null, null), default)).SwapRequestId;
        await w.Swaps(binh).Respond(id, new(true), default);

        w.Clock.UtcNow = w.Local(Fri, 14) - TimeSpan.FromHours(24);
        Assert.Equal(SwapStatus.Expired, (await w.Swaps(manager).Get(id, default)).Status);
        Assert.Equal("SWAP_EXPIRED", (await Assert.ThrowsAsync<AppError>(() => w.Swaps(manager).Approve(id, new(null), default))).Code);
        Assert.Equal(1, await w.Swaps(manager).ExpireDue(default));
        Assert.Equal(0, await w.Swaps(manager).ExpireDue(default));

        var expired = await w.Swaps(an).Get(id, default);
        Assert.Equal(SwapStatus.Expired, expired.Status);
        Assert.False(expired.CanCancel);
        Assert.Equal(new[] { an, binh, manager }.Order(), w.Recipients(ShiftSwaps.Expired).Order());
        Assert.Equal(an, mine.UserId);
    }

    [Fact]
    public async Task RolesAndRequiredFieldsAreEnforced()
    {
        var w = new World();
        var an = w.Staff("An"); var mine = w.Assign(an, w.Evening, Fri);
        var binh = w.Staff("Binh"); var chi = w.Staff("Chi"); var manager = w.User("MANAGER", "Lan"); var op = w.User("OPERATOR", "Op");
        var id = (await w.Swaps(an).Create(new(mine.ShiftAssignmentId, SwapType.Cover, binh, null, null, null), default)).SwapRequestId;
        await w.Swaps(binh).Respond(id, new(true), default);

        Assert.Equal(403, (await Assert.ThrowsAsync<AppError>(() => w.Swaps(an).Approve(id, new(null), default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<AppError>(() => w.Swaps(manager).Create(new(mine.ShiftAssignmentId, SwapType.Cover, binh, null, null, null), default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<AppError>(() => w.Swaps(chi).Get(id, default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<AppError>(() => w.Swaps(op).Get(id, default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<AppError>(() => w.Swaps(chi).Respond(id, new(true), default))).Status);
        Assert.Equal("INVALID_FIELD", (await Assert.ThrowsAsync<DomainException>(() => w.Swaps(manager).Reject(id, new(" "), default))).Code);
        Assert.Equal("TARGET_SHIFT_REQUIRED", (await Assert.ThrowsAsync<DomainException>(() => w.Swaps(an).Create(new(mine.ShiftAssignmentId, SwapType.Swap, binh, null, null, null), default))).Code);
        Assert.Equal("INVALID_REASON", (await Assert.ThrowsAsync<DomainException>(() => w.Swaps(an).Create(new(mine.ShiftAssignmentId, SwapType.Cover, binh, null, "BORED", null), default))).Code);

        var rejected = await w.Swaps(manager).Reject(id, new("Zone B is short that evening"), default);
        Assert.Equal((SwapStatus.Rejected, "Zone B is short that evening"), (rejected.Status, rejected.DecisionNote));
        Assert.Equal(an, mine.UserId);
    }

    private sealed class World
    {
        public MonitoringSetupTests.MemoryStore Store { get; } = new();
        public StaffTaskTests.Clock Clock { get; } = new() { UtcNow = Now };
        public StoreCalendar Calendar { get; } = new(StoreCalendar.Zone(null));
        public Shift Morning { get; } = new() { ShiftId = Guid.NewGuid(), WeekStart = new(2026, 10, 19), Name = "Morning", StartTime = new(6, 0), EndTime = new(14, 0) };
        public Shift Evening { get; } = new() { ShiftId = Guid.NewGuid(), WeekStart = new(2026, 10, 19), Name = "Evening", StartTime = new(14, 0), EndTime = new(22, 0) };
        private readonly Zone zone = new() { ZoneId = Guid.NewGuid(), Code = "B", Name = "Zone B · Checkout", MinStaff = 1 };
        private readonly SwapPlanner planner;
        private readonly TaskNotifier notifier;

        public World()
        {
            foreach (var role in new[] { "ADMIN", "OPERATOR", "MANAGER", "STAFF" }) Store.Values.Add(new Role { RoleId = Guid.NewGuid(), Name = role });
            Store.Values.AddRange([Morning, Evening, zone]);
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
        public ShiftAssignment Assign(Guid user, Shift shift, DateOnly day)
        {
            var assignment = new ShiftAssignment { ShiftAssignmentId = Guid.NewGuid(), UserId = user, ShiftId = shift.ShiftId, WorkDate = day };
            Store.Values.Add(assignment);
            Store.Values.Add(new StaffZoneAssignment { StaffZoneAssignmentId = Guid.NewGuid(), ShiftAssignmentId = assignment.ShiftAssignmentId, ZoneId = zone.ZoneId, AssignedAt = Now });
            return assignment;
        }
        public void Unavailable(Guid user, DateOnly day, int fromHour, int toHour)
            => Store.Values.Add(new StaffUnavailability { UnavailabilityId = Guid.NewGuid(), UserId = user, StartAt = Local(day, fromHour), EndAt = Local(day, toHour), CreatedAt = Now });
        public Guid[] Recipients(string type)
        {
            var ids = Store.Values.OfType<Notification>().Where(n => n.NotificationType == type).Select(n => n.NotificationId).ToHashSet();
            return Store.Values.OfType<NotificationRecipient>().Where(r => ids.Contains(r.NotificationId)).Select(r => r.UserId).Distinct().ToArray();
        }
        public ShiftSwaps Swaps(Guid user)
        {
            var account = Store.Values.OfType<UserAccount>().Single(u => u.UserId == user);
            return new(Store, new Current(user, Store.Values.OfType<Role>().Single(r => r.RoleId == account.RoleId).Name), Clock, Calendar, planner, notifier, new SchemaReady());
        }
    }
    private sealed record Current(Guid UserId, string Role) : ICurrentUser;
    private sealed class SchemaReady : IShiftSwapSchema { public Task<bool> Ready(CancellationToken ct) => Task.FromResult(true); }
}
