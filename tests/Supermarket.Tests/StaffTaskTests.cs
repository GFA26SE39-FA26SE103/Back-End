using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
using AppError = Supermarket.Application.ApplicationException;
namespace Supermarket.Tests;

public sealed class StaffTaskTests
{
    // 10:00 store time (UTC+7); the day shift runs 08:00-16:00 local.
    private static readonly DateTime Now = new(2026, 10, 11, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task EventWithAutomaticTaskPolicyOffersOnlyEligibleZoneStaff()
    {
        var w = new World();
        var a = w.Staff("An", checkedIn: true); var b = w.Staff("Binh", checkedIn: true);
        var otherZone = w.Staff("Chi", checkedIn: true, zone: w.OtherZone);
        var notCheckedIn = w.Staff("Dung", checkedIn: false);
        var busy = w.Staff("Em", checkedIn: true); w.ActiveTasks(busy, 2);
        w.Policy("WARNING");

        var ev = await w.Raise("WARNING");

        var task = Assert.Single(w.Tasks, t => t.EventId == ev.EventId);
        Assert.Equal((WorkTaskStatus.Offered, TaskOrigin.OperationalEvent, "WARNING", w.Zone.ZoneId), (task.Status, task.OriginType, task.Priority, task.ZoneId));
        Assert.Equal("Clear the queue", task.Title); Assert.Equal("Open one more counter", task.Description);
        Assert.Equal(new[] { a, b }.Order(), w.Offers(task).Select(o => o.StaffUserId).Order());
        Assert.DoesNotContain(w.Offers(task), o => o.StaffUserId == otherZone || o.StaffUserId == notCheckedIn || o.StaffUserId == busy);
        var round = Assert.Single(w.Notifications(task, TaskNotifier.Offer));
        Assert.Equal(new[] { a, b }.Order(), w.Recipients(round).Order());
    }

    [Fact]
    public async Task NoPolicyOrApprovalRequiredActionCreatesNoTask()
    {
        var w = new World(); w.Staff("An", checkedIn: true);
        await w.Raise("WARNING");
        Assert.Empty(w.Tasks);
        w.Policy("WARNING", mode: "APPROVAL_REQUIRED");
        await w.Raise("WARNING");
        Assert.Empty(w.Tasks);
    }

    [Theory]
    [InlineData(false, "ACTIVE")]
    [InlineData(true, "DRAFT")]
    public async Task AutomaticPolicyDoesNotDispatchWhenItsRuleIsDisabledOrConfigurationInactive(bool ruleEnabled, string configurationStatus)
    {
        var w = new World(); w.Staff("An", checkedIn: true); w.Policy("WARNING");
        Assert.Single(w.Store.Values.OfType<MonitoringRule>()).Enabled = ruleEnabled;
        Assert.Single(w.Store.Values.OfType<MonitoringConfiguration>()).Status = configurationStatus;

        await w.Raise("WARNING");

        Assert.Empty(w.Tasks);
    }

    [Fact]
    public async Task AmbiguousAutomaticTaskActionsDoNotDispatch()
    {
        var w = new World(); w.Staff("An", checkedIn: true); w.Policy("WARNING");
        var policy = Assert.Single(w.Store.Values.OfType<ResponsePolicy>());
        w.Store.Values.Add(new ResponsePolicyAction { ActionId = Guid.NewGuid(), PolicyId = policy.PolicyId, ActionType = ResponsePolicyRules.CreateOperationalTask,
            ExecutionMode = ResponsePolicyRules.Automatic, ActionParametersJson = "{}", IsEnabled = true });

        await w.Raise("WARNING");

        Assert.Empty(w.Tasks);
    }

    [Fact]
    public async Task PolicyAttachedToANonOperationalRuleDoesNotDispatch()
    {
        var w = new World(); w.Staff("An", checkedIn: true); w.Policy("WARNING");
        var rule = Assert.Single(w.Store.Values.OfType<MonitoringRule>());
        rule.OutcomeType = "INCIDENT"; rule.EventTypeId = null; rule.IncidentTypeId = Guid.NewGuid();

        await w.Raise("WARNING");

        Assert.Empty(w.Tasks);
    }

    [Fact]
    public async Task EscalationUpdatesTheSameTaskInsteadOfCreatingAnother()
    {
        var w = new World(); var a = w.Staff("An", checkedIn: true); w.Policy("WARNING"); w.Policy("CRITICAL");
        var ev = await w.Raise("WARNING");
        ev.Priority = "CRITICAL";
        await w.Dispatch.Raised(ev, "WARNING", default);
        var task = Assert.Single(w.Tasks);
        Assert.Equal("CRITICAL", task.Priority);
        Assert.Single(w.Notifications(task, TaskNotifier.PriorityRaised));
        var offer = Assert.Single(await w.StaffTasks(a).Offers(true, default));
        Assert.Equal("CRITICAL", offer.Priority);
        var view = Assert.Single(await w.Views.Tasks([task], default));
        Assert.Equal("WARNING", view.RaisedFrom);
    }

    [Fact]
    public async Task FirstValidAcceptanceWinsAndOtherOffersAreWithdrawn()
    {
        var w = new World(); var a = w.Staff("An", checkedIn: true); var b = w.Staff("Binh", checkedIn: true); w.Policy("WARNING");
        await w.Raise("WARNING");
        var offerA = w.OfferFor(a); var offerB = w.OfferFor(b);

        var result = await w.StaffTasks(a).Respond(offerA.TaskAssignmentId, new(TaskResponse.HandleNow, null, null), default);
        Assert.Equal(WorkTaskStatus.InProgress, result.Task!.Status);
        Assert.Equal(a, result.Task.AssigneeUserId); Assert.Equal(Now, result.Task.StartedAt);
        Assert.Equal(OfferStatus.Withdrawn, offerB.Status); Assert.Equal("TAKEN_BY_OTHER", offerB.Reason);

        var taken = await Assert.ThrowsAsync<AppError>(() => w.StaffTasks(b).Respond(offerB.TaskAssignmentId, new(TaskResponse.HandleNow, null, null), default));
        Assert.Equal("TASK_ALREADY_TAKEN", taken.Code); Assert.Contains("An", taken.Message);
        var closed = Assert.Single(await w.StaffTasks(b).Offers(false, default));
        Assert.Equal((OfferStatus.Withdrawn, "TAKEN_BY_OTHER"), (closed.Status, closed.ClosedReason));
    }

    [Fact]
    public async Task HandleLaterKeepsResponsibilityWithAPlannedStartInsideTheShift()
    {
        var w = new World(); var a = w.Staff("An", checkedIn: true); w.Policy("WARNING");
        await w.Raise("WARNING");
        var offer = w.OfferFor(a); var staff = w.StaffTasks(a);

        foreach (var invalid in new DateTime?[] { null, Now.AddMinutes(-1), Now.AddHours(7) })
            Assert.Equal("INVALID_PLANNED_START", (await Assert.ThrowsAsync<DomainException>(() => staff.Respond(offer.TaskAssignmentId, new(TaskResponse.HandleLater, invalid, null), default))).Code);

        var accepted = (await staff.Respond(offer.TaskAssignmentId, new(TaskResponse.HandleLater, Now.AddMinutes(15), null), default)).Task!;
        Assert.Equal((WorkTaskStatus.Accepted, Now.AddMinutes(15), (DateTime?)null), (accepted.Status, accepted.PlannedStartAt, accepted.StartedAt));
        Assert.Single(await staff.Tasks(null, default));

        w.Clock.UtcNow = Now.AddMinutes(10);
        var started = await staff.Start(accepted.TaskId, default);
        Assert.Equal((WorkTaskStatus.InProgress, Now.AddMinutes(10)), (started.Status, started.StartedAt));
        Assert.Equal(WorkTaskStatus.InProgress, (await staff.Start(accepted.TaskId, default)).Status);
    }

    [Fact]
    public async Task CannotHandleNeedsAReasonAndAllDeclinedGoesToTheOperator()
    {
        var w = new World(); var a = w.Staff("An", checkedIn: true); var b = w.Staff("Binh", checkedIn: true); var op = w.User("OPERATOR", "Olivia"); w.Policy("WARNING");
        await w.Raise("WARNING");
        Assert.Equal("REASON_REQUIRED", (await Assert.ThrowsAsync<DomainException>(() => w.StaffTasks(a).Respond(w.OfferFor(a).TaskAssignmentId, new(TaskResponse.CannotHandle, null, " "), default))).Code);

        Assert.Null((await w.StaffTasks(a).Respond(w.OfferFor(a).TaskAssignmentId, new(TaskResponse.CannotHandle, null, "Helping a customer"), default)).Task);
        var task = Assert.Single(w.Tasks);
        Assert.Empty(w.Notifications(task, TaskNotifier.NeedsAssignment));
        await w.StaffTasks(b).Respond(w.OfferFor(b).TaskAssignmentId, new(TaskResponse.CannotHandle, null, "On break"), default);
        Assert.Equal([op], w.Recipients(Assert.Single(w.Notifications(task, TaskNotifier.NeedsAssignment))));
        Assert.Equal(WorkTaskStatus.Offered, task.Status);
        Assert.True(Assert.Single(await w.Operator(op).List(null, true, default)).NeedsAssignment);
    }

    [Fact]
    public async Task AcceptingRequiresACurrentCheckInAndRespectsTheWorkloadLimit()
    {
        var w = new World(); var a = w.Staff("An", checkedIn: true); w.Policy("WARNING");
        await w.Raise("WARNING");
        w.ActiveTasks(a, 2);
        Assert.Equal("WORKLOAD_LIMIT", (await Assert.ThrowsAsync<AppError>(() => w.StaffTasks(a).Respond(w.OfferFor(a).TaskAssignmentId, new(TaskResponse.HandleNow, null, null), default))).Code);
        w.Clock.UtcNow = Now.AddHours(8);
        Assert.Equal("NOT_ON_SHIFT", (await Assert.ThrowsAsync<AppError>(() => w.StaffTasks(a).Respond(w.OfferFor(a).TaskAssignmentId, new(TaskResponse.HandleNow, null, null), default))).Code);
    }

    [Fact]
    public async Task RetryReoffersEachIntervalThenExpiresAndAsksTheOperatorOnce()
    {
        var w = new World(); var a = w.Staff("An", checkedIn: true); var op = w.User("OPERATOR", "Olivia"); w.Policy("WARNING");
        await w.Raise("WARNING");
        var task = Assert.Single(w.Tasks);
        Assert.Equal(0, await w.Dispatch.Tick(default));

        var late = w.Staff("Binh", checkedIn: true);
        w.Clock.UtcNow = Now.AddSeconds(60);
        Assert.Equal(1, await w.Dispatch.Tick(default));
        Assert.Equal(2, w.Notifications(task, TaskNotifier.Offer).Count);
        Assert.Equal(new[] { a, late }.Order(), w.Offers(task).Select(o => o.StaffUserId).Order());
        var offer = Assert.Single(await w.StaffTasks(a).Offers(true, default));
        Assert.Equal((2, 3, Now.AddSeconds(120), 2), (offer.Attempt, offer.MaxAttempts, offer.NextAlertAt, offer.Recipients));

        w.Clock.UtcNow = Now.AddSeconds(120); await w.Dispatch.Tick(default);
        Assert.Equal(3, w.Notifications(task, TaskNotifier.Offer).Count);
        Assert.Null(Assert.Single(await w.StaffTasks(a).Offers(true, default)).NextAlertAt);
        w.Clock.UtcNow = Now.AddSeconds(180); await w.Dispatch.Tick(default);
        Assert.All(w.Offers(task), o => Assert.Equal((OfferStatus.Expired, "NO_ACCEPTANCE"), (o.Status, o.Reason)));
        Assert.Equal([op], w.Recipients(Assert.Single(w.Notifications(task, TaskNotifier.NeedsAssignment))));
        w.Clock.UtcNow = Now.AddSeconds(240); await w.Dispatch.Tick(default);
        Assert.Single(w.Notifications(task, TaskNotifier.NeedsAssignment));
        Assert.Equal(3, w.Notifications(task, TaskNotifier.Offer).Count);
    }

    [Fact]
    public async Task RetryUsesSystemSettingsWhenPresent()
    {
        var w = new World(); w.Staff("An", checkedIn: true); w.Policy("WARNING");
        w.Store.Values.Add(new SystemSetting { SettingId = Guid.NewGuid(), SettingKey = TaskRules.RetryIntervalSetting, SettingValue = "30", ValueType = "INTEGER", UpdatedAt = Now });
        w.Store.Values.Add(new SystemSetting { SettingId = Guid.NewGuid(), SettingKey = TaskRules.MaxRetryCountSetting, SettingValue = "1", ValueType = "INTEGER", UpdatedAt = Now });
        await w.Raise("WARNING");
        var task = Assert.Single(w.Tasks);
        w.Clock.UtcNow = Now.AddSeconds(30); await w.Dispatch.Tick(default);
        w.Clock.UtcNow = Now.AddSeconds(60); await w.Dispatch.Tick(default);
        Assert.Equal(2, w.Notifications(task, TaskNotifier.Offer).Count);
        Assert.All(w.Offers(task), o => Assert.Equal(OfferStatus.Expired, o.Status));
    }

    [Fact]
    public async Task ClearedEventWithdrawsOpenOffersButKeepsAcceptedTasks()
    {
        var w = new World(); var a = w.Staff("An", checkedIn: true); var b = w.Staff("Binh", checkedIn: true); w.Policy("WARNING");
        var open = await w.Raise("WARNING");
        var accepted = await w.Raise("WARNING", w.OtherCamera);
        var acceptedTask = w.Tasks.Single(t => t.EventId == accepted.EventId);
        await w.StaffTasks(a).Respond(w.Offers(acceptedTask).Single(o => o.StaffUserId == a).TaskAssignmentId, new(TaskResponse.HandleLater, Now.AddMinutes(10), null), default);

        foreach (var ev in new[] { open, accepted }) { ev.ClearedAt = Now; ev.Status = "CLEARED"; await w.Dispatch.Cleared(ev, default); }

        var openTask = w.Tasks.Single(t => t.EventId == open.EventId);
        Assert.Equal(WorkTaskStatus.Cancelled, openTask.Status);
        Assert.All(w.Offers(openTask), o => Assert.Equal((OfferStatus.Withdrawn, "EVENT_CLEARED"), (o.Status, o.Reason)));
        Assert.Equal(new[] { a, b }.Order(), w.Recipients(Assert.Single(w.Notifications(openTask, TaskNotifier.OfferWithdrawn))).Order());
        Assert.Equal(WorkTaskStatus.Accepted, acceptedTask.Status);
        Assert.Equal([a], w.Recipients(Assert.Single(w.Notifications(acceptedTask, TaskNotifier.EventCleared))));
        var view = Assert.Single(await w.StaffTasks(a).Tasks(null, default));
        Assert.Equal(Now, view.EventClearedAt);
    }

    [Fact]
    public async Task CheckInOpensFifteenMinutesBeforeTheShiftAndClosesAtItsEnd()
    {
        var w = new World(); var a = w.Staff("An", checkedIn: false);
        var assignment = w.Store.Values.OfType<ShiftAssignment>().Single(s => s.UserId == a);
        var staff = w.StaffTasks(a);
        w.Clock.UtcNow = new DateTime(2026, 10, 11, 0, 44, 0, DateTimeKind.Utc); // 07:44 local
        Assert.Equal("CHECK_IN_TOO_EARLY", (await Assert.ThrowsAsync<AppError>(() => staff.CheckIn(assignment.ShiftAssignmentId, default))).Code);
        Assert.False((await staff.Eligibility(default)).CheckedIn);
        w.Clock.UtcNow = new DateTime(2026, 10, 11, 0, 45, 0, DateTimeKind.Utc); // 07:45 local
        var shift = await staff.CheckIn(assignment.ShiftAssignmentId, default);
        Assert.Equal((w.Clock.UtcNow, new DateTime(2026, 10, 11, 1, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Utc)), (shift.CheckInAt!.Value, shift.StartsAt, shift.EndsAt));
        Assert.Equal(w.Zone.Name, Assert.Single(shift.Zones).ZoneName);
        // Checked in before the start but not yet on shift.
        Assert.False((await staff.Eligibility(default)).CheckedIn);
        w.Clock.UtcNow = Now;
        var eligibility = await staff.Eligibility(default);
        Assert.True(eligibility.CheckedIn); Assert.Equal((0, 2), (eligibility.ActiveTasks, eligibility.MaxActiveTasks));

        var other = w.Staff("Binh", checkedIn: false);
        w.Clock.UtcNow = Now.AddHours(6);
        Assert.Equal("SHIFT_ENDED", (await Assert.ThrowsAsync<AppError>(() => w.StaffTasks(other).CheckIn(w.Store.Values.OfType<ShiftAssignment>().Single(s => s.UserId == other).ShiftAssignmentId, default))).Code);
        Assert.Equal("NOT_FOUND", (await Assert.ThrowsAsync<AppError>(() => staff.CheckIn(w.Store.Values.OfType<ShiftAssignment>().Single(s => s.UserId == other).ShiftAssignmentId, default))).Code);
    }

    [Fact]
    public void OvernightShiftEndsTheNextDay()
    {
        var calendar = new StoreCalendar(StoreCalendar.Zone(null));
        var (start, end) = calendar.Window(new DateOnly(2026, 10, 11), new TimeOnly(22, 0), new TimeOnly(6, 0));
        Assert.Equal(new DateTime(2026, 10, 11, 15, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2026, 10, 11, 23, 0, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public async Task OperatorAssignsManuallyAtAcceptedAndStaffOnlySeeOwnData()
    {
        var w = new World(); var a = w.Staff("An", checkedIn: true); var b = w.Staff("Binh", checkedIn: true, zone: w.OtherZone); var op = w.User("OPERATOR", "Olivia"); w.Policy("WARNING");
        await w.Raise("WARNING");
        var task = Assert.Single(w.Tasks);

        var candidates = await w.Operator(op).AssignableStaff(task.TaskId, default);
        Assert.Equal([a, b], candidates.Select(c => c.UserId).ToArray()); Assert.True(candidates[0].AssignedToZone); Assert.False(candidates[1].AssignedToZone);
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<AppError>(() => w.Operator(a).Assign(task.TaskId, new(b, null), default))).Code);

        var assigned = await w.Operator(op).Assign(task.TaskId, new(b, "Nearest free staff"), default);
        Assert.Equal((WorkTaskStatus.Accepted, b, TaskAssignmentType.Manual, (DateTime?)null), (assigned.Status, assigned.AssigneeUserId, assigned.AssignmentType, assigned.PlannedStartAt));
        Assert.Equal((OfferStatus.Withdrawn, "MANUALLY_ASSIGNED"), (w.OfferFor(a).Status, w.OfferFor(a).Reason));
        Assert.Equal([b], w.Recipients(Assert.Single(w.Notifications(task, TaskNotifier.Assigned))));
        Assert.Single(await w.Inbox(b).List(default), n => n.Type == TaskNotifier.Assigned);
        Assert.Equal(1, await w.Inbox(b).ReadAll(default));
        Assert.All(await w.Inbox(b).List(default), n => Assert.NotNull(n.ReadAt));

        // An offered Staff member may still read the task; an uninvolved one may not.
        Assert.Equal(task.TaskId, (await w.Operator(a).Get(task.TaskId, default)).TaskId);
        var stranger = w.Staff("Chi", checkedIn: true);
        Assert.Equal("NOT_FOUND", (await Assert.ThrowsAsync<AppError>(() => w.Operator(stranger).Get(task.TaskId, default))).Code);
        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<AppError>(() => w.StaffTasks(op).Tasks(null, default))).Code);
    }

    [Fact]
    public async Task OperatorAssignmentRejectsAStaleTaskWithoutChangingItsAssigneeOrOffers()
    {
        var w = new World(); var a = w.Staff("An", checkedIn: true); var op = w.User("OPERATOR", "Olivia"); w.Policy("WARNING");
        await w.Raise("WARNING");
        var task = Assert.Single(w.Tasks);
        var staleVersion = task.UpdatedAt;
        // A Staff acceptance occurs after the Operator loaded the board.
        await w.StaffTasks(a).Respond(w.OfferFor(a).TaskAssignmentId, new(TaskResponse.HandleLater, Now.AddMinutes(10), null), default);
        Assert.Equal("TASK_CHANGED", (await Assert.ThrowsAsync<AppError>(() => w.Operator(op).Assign(task.TaskId, new(a, null, staleVersion), default))).Code);
        Assert.Equal(a, task.CurrentAssigneeUserId);
        Assert.Equal(TaskAssignmentType.Offer, (await w.Operator(op).Get(task.TaskId, default)).AssignmentType);
        Assert.Empty(w.Notifications(task, TaskNotifier.Assigned));
    }

    [Fact]
    public async Task OutsideZoneAssignmentAndReassignmentRequireAnAuditReason()
    {
        var w = new World(); var a = w.Staff("An", checkedIn: true); var b = w.Staff("Binh", checkedIn: true, zone: w.OtherZone);
        var op = w.User("OPERATOR", "Olivia"); w.Policy("WARNING"); await w.Raise("WARNING");
        var task = Assert.Single(w.Tasks);
        Assert.Equal("REASON_REQUIRED", (await Assert.ThrowsAsync<DomainException>(() => w.Operator(op).Assign(task.TaskId, new(b, " ", task.UpdatedAt), default))).Code);
        Assert.Null(task.CurrentAssigneeUserId);
        await w.Operator(op).Assign(task.TaskId, new(a, null, task.UpdatedAt), default);
        Assert.Equal("REASON_REQUIRED", (await Assert.ThrowsAsync<DomainException>(() => w.Operator(op).Assign(task.TaskId, new(b, null, task.UpdatedAt), default))).Code);
        Assert.Equal(a, task.CurrentAssigneeUserId);
        var assigned = await w.Operator(op).Assign(task.TaskId, new(b, "Cross-zone support", task.UpdatedAt), default);
        Assert.Equal(b, assigned.AssigneeUserId);
        Assert.Equal("Cross-zone support", w.Store.Values.OfType<TaskAssignmentHistory>().Single(h => h.AssignmentType == TaskAssignmentType.Manual && h.StaffUserId == b).Reason);
    }

    [Fact]
    public async Task NeedsAssignmentFilterIsAppliedBeforeTheTwoHundredTaskLimit()
    {
        var w = new World(); var assignee = w.Staff("An", checkedIn: true); var op = w.User("OPERATOR", "Olivia");
        var oldestUnassigned = new WorkTask { TaskId = Guid.NewGuid(), ZoneId = w.Zone.ZoneId, Title = "Old task needing assignment",
            Status = WorkTaskStatus.Offered, CreatedAt = Now.AddDays(-1) };
        w.Store.Values.Add(oldestUnassigned);
        for (var i = 0; i < 200; i++)
            w.Store.Values.Add(new WorkTask { TaskId = Guid.NewGuid(), ZoneId = w.Zone.ZoneId, Title = $"Assigned {i}", Status = WorkTaskStatus.Offered,
                CurrentAssigneeUserId = assignee, CreatedAt = Now.AddMinutes(i) });

        var result = await w.Operator(op).List(null, true, default);

        Assert.Equal(oldestUnassigned.TaskId, Assert.Single(result).TaskId);
        Assert.True(result[0].NeedsAssignment);
    }

    [Fact]
    public async Task OperatorCannotReassignWorkAlreadyInProgressEvenWithTheCurrentVersion()
    {
        var w = new World(); var a = w.Staff("An", checkedIn: true); var b = w.Staff("Binh", checkedIn: true);
        var op = w.User("OPERATOR", "Olivia"); w.Policy("WARNING"); await w.Raise("WARNING");
        var task = Assert.Single(w.Tasks);
        await w.StaffTasks(a).Respond(w.OfferFor(a).TaskAssignmentId, new(TaskResponse.HandleNow, null, null), default);
        Assert.Equal("TASK_NOT_ASSIGNABLE", (await Assert.ThrowsAsync<AppError>(() => w.Operator(op).Assign(task.TaskId, new(b, "Change Staff", task.UpdatedAt), default))).Code);
        Assert.Equal(a, task.CurrentAssigneeUserId);
        Assert.Equal(WorkTaskStatus.InProgress, task.Status);
    }

    [Fact]
    public async Task MissingTaskSchemaDisablesDispatchButNotMonitoring()
    {
        var w = new World(schemaReady: false); w.Staff("An", checkedIn: true); w.Policy("WARNING");
        await w.Raise("WARNING");
        Assert.Empty(w.Tasks);
        Assert.Equal(0, await w.Dispatch.Tick(default));
        Assert.Equal("TASK_SCHEMA_NOT_READY", (await Assert.ThrowsAsync<AppError>(() => w.StaffTasks(w.Store.Values.OfType<UserAccount>().First().UserId).Offers(true, default))).Code);
    }

    [Fact]
    public async Task EpisodeWriterOpensOneTaskAndRaisesItsPriorityOnEscalation()
    {
        var w = new World(); var a = w.Staff("An", checkedIn: true);
        var eventTypeId = Guid.NewGuid(); var configId = Guid.NewGuid();
        var rule = new MonitoringRuleSnapshot(Guid.NewGuid(), Guid.NewGuid(), "LONG_QUEUE", "Queue", "QUEUE_LENGTH", "PEOPLE", 1, 2, 1, 10, eventTypeId, "LONG_QUEUE", 1);
        w.Store.Values.Add(new MonitoringConfiguration { ConfigId = configId, ZoneId = w.Zone.ZoneId, Name = "Episode monitoring", Status = "ACTIVE" });
        w.Store.Values.Add(new MonitoringRule { RuleId = rule.RuleId, ConfigId = configId, EventTypeId = eventTypeId, OutcomeType = "OPERATIONAL_EVENT", Enabled = true });
        w.Policy("WARNING", rule.RuleId); w.Policy("CRITICAL", rule.RuleId);
        var writer = new MonitoringIncidentWriter(w.Store, new Snapshots(), new Queries(), w.Clock, w.Dispatch);
        var zone = new MonitoringZoneSnapshot(w.Zone.ZoneId, w.Zone.Name, configId, Now, [], .5m, [rule]);
        var snapshot = new MonitoringCameraSnapshot(w.Camera.CameraId, new(), "f", [zone]);
        var session = Guid.NewGuid();
        IncidentObservationCommand Command(long seq, int queue, string? severity, bool clear = false)
        {
            var measurement = new AiZoneMeasurement(zone.ZoneId, zone.ConfigId, zone.ConfigVersion, queue, queue);
            return new(snapshot, zone, rule, new(1, session, session, seq, Now.AddSeconds(seq), seq * 1000, "f", [measurement]), measurement,
                new(new(), severity, 1000, 0, clear ? "RECOVERY_SUSTAINED" : "THRESHOLD_SUSTAINED", true, clear), true);
        }
        await writer.Apply(Command(1, 1, "WARNING"), default);
        await writer.Apply(Command(2, 1, "WARNING"), default);
        await writer.Apply(Command(3, 3, "CRITICAL"), default);
        var task = Assert.Single(w.Tasks);
        Assert.Equal("CRITICAL", task.Priority);
        Assert.Single(w.Offers(task));
        await w.StaffTasks(a).Respond(w.OfferFor(a).TaskAssignmentId, new(TaskResponse.HandleNow, null, null), default);
        await writer.Apply(Command(4, 0, null, true), default);
        Assert.Equal(WorkTaskStatus.InProgress, task.Status);
        Assert.Single(w.Notifications(task, TaskNotifier.EventCleared));
    }

    private sealed class World
    {
        public MonitoringSetupTests.MemoryStore Store { get; } = new();
        public Clock Clock { get; } = new() { UtcNow = Now };
        public StoreCalendar Calendar { get; } = new(StoreCalendar.Zone(null));
        public Zone Zone { get; }
        public Zone OtherZone { get; }
        public Camera Camera { get; }
        public Camera OtherCamera { get; }
        public TaskDispatch Dispatch { get; }
        public TaskViews Views { get; }
        public OperatorQueries TaskQueries { get; }
        private readonly Schema schema;
        private readonly StaffRoster roster;
        private readonly TaskNotifier notifier;
        private readonly Guid ruleId = Guid.NewGuid(), eventTypeId = Guid.NewGuid(), shiftId = Guid.NewGuid();

        public World(bool schemaReady = true)
        {
            schema = new Schema { IsReady = schemaReady };
            var floor = Guid.NewGuid();
            Zone = new Zone { ZoneId = Guid.NewGuid(), FloorId = floor, Code = "B", Name = "Zone B · Checkout" };
            OtherZone = new Zone { ZoneId = Guid.NewGuid(), FloorId = floor, Code = "C", Name = "Zone C · Produce" };
            Camera = new Camera { CameraId = Guid.NewGuid(), FloorId = floor, Code = "CAM-03", Name = "Checkout 3" };
            OtherCamera = new Camera { CameraId = Guid.NewGuid(), FloorId = floor, Code = "CAM-04", Name = "Checkout 4" };
            foreach (var role in new[] { "ADMIN", "OPERATOR", "MANAGER", "STAFF" }) Store.Values.Add(new Role { RoleId = Guid.NewGuid(), Name = role });
            var configuration = new MonitoringConfiguration { ConfigId = Guid.NewGuid(), ZoneId = Zone.ZoneId, Name = "Queue monitoring", Status = "ACTIVE" };
            var rule = new MonitoringRule { RuleId = ruleId, ConfigId = configuration.ConfigId, EventTypeId = eventTypeId, OutcomeType = "OPERATIONAL_EVENT", Enabled = true };
            Store.Values.AddRange([Zone, OtherZone, Camera, OtherCamera, configuration, rule,
                new OperationalEventType { EventTypeId = eventTypeId, Code = "LONG_QUEUE", Name = "Long queue", MeasurementType = "QUEUE_LENGTH" },
                new Shift { ShiftId = shiftId, WeekStart = new(2026, 10, 5), Name = "Morning", StartTime = new(8, 0), EndTime = new(16, 0) }]);
            roster = new StaffRoster(Store, Calendar);
            notifier = new TaskNotifier(Store, Clock);
            Dispatch = new TaskDispatch(Store, roster, notifier, Clock, schema);
            Views = new TaskViews(Store, Dispatch);
            TaskQueries = new OperatorQueries(Store);
        }
        public IEnumerable<WorkTask> Tasks => Store.Values.OfType<WorkTask>().Where(t => t.EventId != null);
        public Guid User(string role, string name)
        {
            var user = new UserAccount { UserId = Guid.NewGuid(), RoleId = Store.Values.OfType<Role>().Single(r => r.Name == role).RoleId, FullName = name, Email = $"{name}@test", Status = "ACTIVE" };
            Store.Values.Add(user);
            return user.UserId;
        }
        public Guid Staff(string name, bool checkedIn, Zone? zone = null)
        {
            var id = User("STAFF", name);
            var assignment = new ShiftAssignment { ShiftAssignmentId = Guid.NewGuid(), UserId = id, ShiftId = shiftId, WorkDate = new(2026, 10, 11), CheckInAt = checkedIn ? Now.AddHours(-2) : null };
            Store.Values.Add(assignment);
            Store.Values.Add(new StaffZoneAssignment { StaffZoneAssignmentId = Guid.NewGuid(), ShiftAssignmentId = assignment.ShiftAssignmentId, ZoneId = (zone ?? Zone).ZoneId, AssignedAt = Now.AddHours(-3) });
            return id;
        }
        public void ActiveTasks(Guid user, int count)
        {
            for (var i = 0; i < count; i++)
                Store.Values.Add(new WorkTask { TaskId = Guid.NewGuid(), ZoneId = Zone.ZoneId, Title = "Busy", Status = WorkTaskStatus.InProgress, CurrentAssigneeUserId = user });
        }
        public void Policy(string priority, Guid? rule = null, string mode = "AUTOMATIC")
        {
            var policy = new ResponsePolicy { PolicyId = Guid.NewGuid(), RuleId = rule ?? ruleId, EventPriority = priority };
            Store.Values.Add(policy);
            Store.Values.Add(new ResponsePolicyAction { ActionId = Guid.NewGuid(), PolicyId = policy.PolicyId, ActionType = "CREATE_OPERATIONAL_TASK", ExecutionMode = mode,
                ActionParametersJson = """{"title":"Clear the queue","instructions":"Open one more counter"}""" });
        }
        public async Task<OperationalEvent> Raise(string priority, Camera? camera = null)
        {
            var ev = new OperationalEvent { EventId = Guid.NewGuid(), CameraId = (camera ?? Camera).CameraId, ZoneId = Zone.ZoneId, RuleId = ruleId, EventTypeId = eventTypeId,
                Priority = priority, MetricValue = 7, DetectedAt = Clock.UtcNow, LastObservedAt = Clock.UtcNow, Status = "ACTIVE",
                MetadataJson = """{"measurementMode":"QUEUE_LENGTH","thresholdUnit":"PEOPLE"}""" };
            Store.Values.Add(ev);
            await Dispatch.Raised(ev, null, default);
            return ev;
        }
        public List<TaskAssignmentHistory> Offers(WorkTask task) => Store.Values.OfType<TaskAssignmentHistory>().Where(a => a.TaskId == task.TaskId && a.AssignmentType == TaskAssignmentType.Offer).ToList();
        public TaskAssignmentHistory OfferFor(Guid staff) => Store.Values.OfType<TaskAssignmentHistory>().Single(a => a.StaffUserId == staff && a.AssignmentType == TaskAssignmentType.Offer);
        public List<Notification> Notifications(WorkTask task, string type) => Store.Values.OfType<Notification>().Where(n => n.TaskId == task.TaskId && n.NotificationType == type).ToList();
        public Guid[] Recipients(Notification notification) => Store.Values.OfType<NotificationRecipient>().Where(r => r.NotificationId == notification.NotificationId).Select(r => r.UserId).ToArray();
        public StaffTasks StaffTasks(Guid user) => new(Store, Current(user), Clock, Calendar, roster, Dispatch, Views, schema);
        public NotificationInbox Inbox(Guid user) => new(new NotificationInboxTests.StoreAdapter(Store), Current(user), Clock);
        public OperatorTasks Operator(Guid user) => new(Store, Current(user), Clock, roster, Dispatch, notifier, Views, schema, TaskQueries);
        private User Current(Guid id)
        {
            var user = Store.Values.OfType<UserAccount>().Single(u => u.UserId == id);
            return new User(id, Store.Values.OfType<Role>().Single(r => r.RoleId == user.RoleId).Name);
        }
    }
    private sealed record User(Guid UserId, string Role) : ICurrentUser;
    private sealed class Snapshots : IMonitoringSnapshotReader
    {
        public Task<MonitoringCameraSnapshot[]> ReadAll(CancellationToken ct) => Task.FromResult(Array.Empty<MonitoringCameraSnapshot>());
        public Task<bool> IsCurrent(MonitoringCameraSnapshot s, Guid z, CancellationToken ct) => Task.FromResult(true);
    }
    private sealed class Queries : IMonitoringIncidentQueries
    {
        public Task EnsureSchema(CancellationToken ct) => Task.CompletedTask;
        public Task<DateTime?> LatestEndedAt(Guid zone, Guid type, CancellationToken ct) => Task.FromResult<DateTime?>(null);
        public Task<IncidentFeedView> PageForCamera(Guid camera, IncidentFeedQuery q, CancellationToken ct) => throw new NotSupportedException();
    }
    internal sealed class Clock : IClock { public DateTime UtcNow { get; set; } }
    private sealed class Schema : ITaskSchema { public bool IsReady; public Task<bool> Ready(CancellationToken ct) => Task.FromResult(IsReady); }
    internal sealed class OperatorQueries(MonitoringSetupTests.MemoryStore store) : IOperatorTaskQueries
    {
        public async Task<Guid[]> NeedingAssignmentIds(int limit, CancellationToken ct)
        {
            var openOfferTaskIds = (await store.List<TaskAssignmentHistory>(a => a.Status == OfferStatus.Offered, ct)).Select(a => a.TaskId).ToHashSet();
            return (await store.List<WorkTask>(t => t.Status == WorkTaskStatus.Offered && t.CurrentAssigneeUserId == null, ct))
                .Where(t => !openOfferTaskIds.Contains(t.TaskId)).OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.TaskId)
                .Take(limit).Select(t => t.TaskId).ToArray();
        }
    }
}
