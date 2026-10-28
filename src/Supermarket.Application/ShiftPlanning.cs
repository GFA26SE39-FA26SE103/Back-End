using Supermarket.Domain;
using Store = Supermarket.Domain.Supermarket;
namespace Supermarket.Application;

/// <summary>
/// Manager shift planning (MF-04, BR-02/33/34/35): weekly Work Shifts, DRAFT assignments of active Staff to a shift and one Zone,
/// publishing a date range (blocked while any Zone is below Zone.min_staff), edits and cancellation before a shift starts,
/// and Staff leave (StaffUnavailability). Staff only see SCHEDULED (published) assignments.
/// </summary>
public sealed class ShiftPlanning(ISetupStore store, ICurrentUser current, IClock clock, StoreCalendar calendar, SwapPlanner planner, TaskNotifier notifier, IShiftSwapSchema schema)
{
    public const string Published = "SHIFT_PUBLISHED", Changed = "SHIFT_CHANGED", Removed = "SHIFT_CANCELLED";

    /// <summary>Sets the Manager-owned Zone staffing baseline used by coverage checks for every active Work Shift.</summary>
    public Task<ZoneView> SetZoneMinimumStaff(Guid zoneId, ZoneMinimumStaffRequest request, CancellationToken ct)
    {
        UseCase.Manager(current);
        Rules.MinimumStaff(request.MinStaff);
        return store.Transaction(async () =>
        {
            var zone = UseCase.Found(await store.Find<Zone>(zoneId, ct));
            if (request.ExpectedUpdatedAt is { } expected && expected != zone.UpdatedAt)
                throw new ApplicationException("ZONE_CHANGED", "Zone staffing changed; reload before saving.");
            zone.MinStaff = request.MinStaff;
            await store.Update(zone, ct);
            return StoreSetup.View(zone);
        }, ct);
    }

    public async Task<WorkShiftView[]> WorkShifts(DateOnly? weekStart, CancellationToken ct)
    {
        UseCase.Viewer(current);
        await schema.Require(ct);
        var week = weekStart ?? ShiftRules.WeekStart(calendar.Today(clock.UtcNow));
        ShiftRules.Monday(week);
        var shifts = await store.List<Shift>(s => s.WeekStart == week, ct);
        var live = (await store.List<ShiftAssignment>(a => a.Status != ShiftAssignmentStatus.Cancelled, ct)).Select(a => a.ShiftId).ToHashSet();
        return shifts.OrderBy(s => s.StartTime).ThenBy(s => s.Name).Select(s => WorkShift(s, live.Contains(s.ShiftId))).ToArray();
    }

    public async Task<WorkShiftView> CreateWorkShift(CreateWorkShiftRequest request, CancellationToken ct)
    {
        UseCase.Manager(current);
        await schema.Require(ct);
        ShiftRules.Monday(request.WeekStart);
        var name = ShiftRules.WorkShift(request.Name, request.StartTime, request.EndTime);
        var shift = await store.Transaction(async () =>
        {
            var market = (await store.List<Store>(null, ct)).OrderBy(s => s.CreatedAt).FirstOrDefault()
                ?? throw new ApplicationException("SUPERMARKET_REQUIRED", "Create the supermarket before Work Shifts.", 422);
            UseCase.Unique((await store.List<Shift>(s => s.SupermarketId == market.SupermarketId && s.WeekStart == request.WeekStart && s.Name == name, ct)).Count > 0);
            var row = new Shift { ShiftId = Guid.NewGuid(), SupermarketId = market.SupermarketId, Name = name, StartTime = request.StartTime, EndTime = request.EndTime,
                WeekStart = request.WeekStart, Status = WorkShiftStatus.Active };
            await store.Add(row, ct);
            return row;
        }, ct);
        return WorkShift(UseCase.Found(await store.Find<Shift>(shift.ShiftId, ct)), false);
    }

    public async Task<WorkShiftView[]> CopyWorkShiftWeek(CopyWorkShiftWeekRequest request, CancellationToken ct)
    {
        UseCase.Manager(current);
        await schema.Require(ct);
        ShiftRules.Monday(request.SourceWeekStart); ShiftRules.Monday(request.TargetWeekStart);
        Rules.Require(request.SourceWeekStart != request.TargetWeekStart, "INVALID_WEEK_RANGE", "Source and target weeks must be different.");
        await store.Transaction(async () =>
        {
            var source = await store.List<Shift>(s => s.WeekStart == request.SourceWeekStart && s.Status == WorkShiftStatus.Active, ct);
            if (source.Count == 0) throw new ApplicationException("SOURCE_WEEK_EMPTY", "The source week has no active Work Shifts to copy.", 422);
            if ((await store.List<Shift>(s => s.WeekStart == request.TargetWeekStart, ct)).Count > 0)
                throw new ApplicationException("TARGET_WEEK_NOT_EMPTY", "The target week already has Work Shifts.");
            foreach (var shift in source)
                await store.Add(new Shift { ShiftId = Guid.NewGuid(), SupermarketId = shift.SupermarketId, WeekStart = request.TargetWeekStart,
                    Name = shift.Name, StartTime = shift.StartTime, EndTime = shift.EndTime, Status = WorkShiftStatus.Active }, ct);
            return 0;
        }, ct);
        return await WorkShifts(request.TargetWeekStart, ct);
    }

    public async Task<WorkShiftView> UpdateWorkShift(Guid id, UpdateWorkShiftRequest request, CancellationToken ct)
    {
        UseCase.Manager(current);
        await schema.Require(ct);
        if (request.Status is not null) Rules.Status(request.Status, WorkShiftStatus.Active, WorkShiftStatus.Inactive);
        var inUse = await store.Transaction(async () =>
        {
            var shift = UseCase.Found(await store.Find<Shift>(id, ct));
            var used = (await store.List<ShiftAssignment>(a => a.ShiftId == id && a.Status != ShiftAssignmentStatus.Cancelled, ct)).Count > 0;
            var start = request.StartTime ?? shift.StartTime; var end = request.EndTime ?? shift.EndTime;
            if (used && (start != shift.StartTime || end != shift.EndTime))
                throw new ApplicationException("SHIFT_IN_USE", "Work Shift times cannot change after assignments use it in this week.");
            var name = ShiftRules.WorkShift(request.Name ?? shift.Name, start, end);
            UseCase.Unique((await store.List<Shift>(s => s.SupermarketId == shift.SupermarketId && s.WeekStart == shift.WeekStart && s.Name == name && s.ShiftId != id, ct)).Count > 0);
            shift.Name = name; shift.StartTime = start; shift.EndTime = end; shift.Status = request.Status ?? shift.Status;
            await store.Update(shift, ct);
            return used;
        }, ct);
        return WorkShift(UseCase.Found(await store.Find<Shift>(id, ct)), inUse);
    }

    /// <summary>Assignments and BR-02 coverage for a date range; defaults to the current local Monday–Sunday week.</summary>
    public async Task<ScheduleView> Schedule(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        UseCase.Viewer(current);
        await schema.Require(ct);
        var today = calendar.Today(clock.UtcNow);
        var start = from ?? today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var end = to ?? start.AddDays(6);
        ShiftRules.Range(start, end);
        var context = await planner.Load(start, end, ct);
        var rows = context.Assignments.Where(a => a.WorkDate >= start && a.WorkDate <= end).ToList();
        var userIds = rows.Select(a => a.UserId).Distinct().ToList();
        var names = (await store.List<UserAccount>(u => userIds.Contains(u.UserId), ct)).ToDictionary(u => u.UserId, u => u.FullName);
        var zones = await ActiveZones(context, ct);
        var now = clock.UtcNow;
        var pending = context.Pending.Where(r => r.ExpiresAt is null || r.ExpiresAt > now).ToList();
        var views = rows.Select(a => planner.Slot(context, a)).OfType<SwapSlot>().OrderBy(s => s.StartsAt).ThenBy(s => names.GetValueOrDefault(s.Assignment.UserId))
            .Select(s => new ScheduledShiftView(s.Assignment.ShiftAssignmentId, s.Assignment.UserId, names.GetValueOrDefault(s.Assignment.UserId) ?? "", s.Shift.ShiftId, s.Shift.Name,
                s.Assignment.WorkDate, s.StartsAt, s.EndsAt, s.Assignment.Status, s.Assignment.CheckInAt,
                SwapPlanner.ZoneIds(context, s.Assignment.ShiftAssignmentId).Where(zones.ContainsKey).Select(z => new ShiftZoneView(z, zones[z].Name)).ToArray(),
                Editable(s, now),
                pending.FirstOrDefault(r => r.RequesterShiftAssignmentId == s.Assignment.ShiftAssignmentId || r.TargetShiftAssignmentId == s.Assignment.ShiftAssignmentId)?.SwapRequestId,
                s.Assignment.UpdatedAt))
            .ToArray();
        var coverage = Coverage(context, zones, start, end);
        return new(start, end, views, coverage, rows.Count(a => a.Status == ShiftAssignmentStatus.Draft), coverage.Count(c => c.BelowMinimum));
    }

    /// <summary>Every active Staff member with the BR-34/35 problems that would block assigning them to this shift.</summary>
    public async Task<StaffOptionView[]> StaffOptions(Guid shiftId, DateOnly workDate, Guid? replacingAssignmentId, CancellationToken ct)
    {
        UseCase.Manager(current);
        await schema.Require(ct);
        var context = await planner.Load(workDate, workDate, ct);
        var shift = context.Shifts.GetValueOrDefault(shiftId) ?? throw new ApplicationException("NOT_FOUND", "The resource was not found.", 404);
        RequireWorkShiftWeek(shift, workDate);
        var slot = planner.Slot(context, new ShiftAssignment { ShiftAssignmentId = replacingAssignmentId ?? Guid.NewGuid(), ShiftId = shift.ShiftId, WorkDate = workDate })!;
        return context.Staff.Values.Select(user =>
        {
            var problems = planner.PersonChecks(context, user.FullName, user.UserId, slot, null).Where(c => !c.Passed).Select(c => c.Message).ToList();
            if (context.Assignments.Any(a => a.UserId == user.UserId && a.ShiftId == shiftId && a.WorkDate == workDate))
                problems.Insert(0, $"{user.FullName} is already on this shift.");
            return new StaffOptionView(user.UserId, user.FullName, problems.Count == 0, planner.WeeklyHours(context, user.UserId, slot, null), problems.ToArray());
        }).OrderByDescending(o => o.Eligible).ThenBy(o => o.WeeklyHoursAfter).ThenBy(o => o.FullName).ToArray();
    }

    /// <summary>Adds a DRAFT assignment; Staff see it once the range is published.</summary>
    public async Task<ScheduledShiftView> Assign(AssignShiftRequest request, CancellationToken ct)
    {
        UseCase.Manager(current);
        await schema.Require(ct);
        var zoneIds = ZoneList(request.ZoneIds);
        var id = await store.Transaction(async () =>
        {
            var context = await planner.Load(request.WorkDate, request.WorkDate, ct);
            var shift = context.Shifts.GetValueOrDefault(request.ShiftId) ?? throw new ApplicationException("NOT_FOUND", "The Work Shift was not found.", 404);
            RequireWorkShiftWeek(shift, request.WorkDate);
            if (shift.Status != WorkShiftStatus.Active) throw new ApplicationException("SHIFT_INACTIVE", "Activate the Work Shift before assigning Staff.");
            var user = context.Staff.GetValueOrDefault(request.UserId) ?? throw new ApplicationException("NOT_STAFF", "Only active Staff accounts can be scheduled.", 422);
            await RequireZones(zoneIds, ct);
            var existing = (await store.List<ShiftAssignment>(a => a.UserId == request.UserId && a.ShiftId == request.ShiftId && a.WorkDate == request.WorkDate, ct)).SingleOrDefault();
            if (existing is not null && existing.Status != ShiftAssignmentStatus.Cancelled)
                throw new ApplicationException("ALREADY_ASSIGNED", $"{user.FullName} is already on this shift.");
            var row = existing ?? new ShiftAssignment { ShiftAssignmentId = Guid.NewGuid(), UserId = request.UserId, ShiftId = request.ShiftId, WorkDate = request.WorkDate };
            var slot = planner.Slot(context, row)!;
            Future(slot);
            Allowed(planner.PersonChecks(context, user.FullName, user.UserId, slot, null));
            row.Status = ShiftAssignmentStatus.Draft; row.CheckInAt = null; row.CheckOutAt = null;
            // A cancelled row is reused because (user, shift, work date) is unique.
            if (existing is null) await store.Add(row, ct); else await store.Update(row, ct);
            await SetZones(row.ShiftAssignmentId, zoneIds, ct);
            return row.ShiftAssignmentId;
        }, ct);
        return await View(id, ct);
    }

    public async Task<ScheduledShiftView> Update(Guid id, UpdateAssignmentRequest request, CancellationToken ct)
    {
        UseCase.Manager(current);
        await schema.Require(ct);
        var zoneIds = request.ZoneIds is null ? null : ZoneList(request.ZoneIds);
        await store.Transaction(async () =>
        {
            var row = UseCase.Found(await store.Find<ShiftAssignment>(id, ct));
            var context = await planner.Load(row.WorkDate, row.WorkDate, ct);
            var slot = UseCase.Found(planner.Slot(context, row));
            await Changeable(slot, ct);
            var previousUser = row.UserId;
            if (request.UserId is { } userId && userId != row.UserId)
            {
                var user = context.Staff.GetValueOrDefault(userId) ?? throw new ApplicationException("NOT_STAFF", "Only active Staff accounts can be scheduled.", 422);
                if ((await store.List<ShiftAssignment>(a => a.UserId == userId && a.ShiftId == row.ShiftId && a.WorkDate == row.WorkDate, ct)).Count > 0)
                    throw new ApplicationException("ALREADY_ASSIGNED", $"{user.FullName} already has a row for this shift; assign or reactivate it directly.");
                Allowed(planner.PersonChecks(context, user.FullName, userId, slot, null));
                row.UserId = userId;
                await store.Update(row, ct);
            }
            if (zoneIds is not null)
            {
                await RequireZones(zoneIds, ct);
                await SetZones(row.ShiftAssignmentId, zoneIds, ct);
            }
            if (row.Status == ShiftAssignmentStatus.Scheduled)
            {
                var when = Describe(slot);
                if (previousUser != row.UserId)
                {
                    await notifier.Broadcast(Removed, "Shift removed", $"You are no longer scheduled for {when}.", [previousUser], "SHIFT_ASSIGNEE", ct);
                    await notifier.Broadcast(Changed, "New shift", $"You are scheduled for {when}.", [row.UserId], "SHIFT_ASSIGNEE", ct);
                }
                else if (zoneIds is not null)
                    await notifier.Broadcast(Changed, "Shift zone changed", $"Your zone for {when} changed.", [row.UserId], "SHIFT_ASSIGNEE", ct);
            }
            return 0;
        }, ct);
        return await View(id, ct);
    }

    /// <summary>Cancels an assignment before it starts; pending swap requests on it are cancelled too.</summary>
    public async Task<ScheduledShiftView> Cancel(Guid id, CancelAssignmentRequest request, CancellationToken ct)
    {
        UseCase.Manager(current);
        await schema.Require(ct);
        Rules.Optional(request.Reason, 1000, "Reason");
        await store.Transaction(async () =>
        {
            var row = UseCase.Found(await store.Find<ShiftAssignment>(id, ct));
            var context = await planner.Load(row.WorkDate, row.WorkDate, ct);
            var slot = UseCase.Found(planner.Slot(context, row));
            Editable(slot, clock.UtcNow, throwIfNot: true);
            var now = clock.UtcNow; var wasPublished = row.Status == ShiftAssignmentStatus.Scheduled;
            row.Status = ShiftAssignmentStatus.Cancelled;
            await store.Update(row, ct);
            await SetZones(row.ShiftAssignmentId, [], ct);
            var pending = SwapStatus.Pending;
            foreach (var swap in await store.List<ShiftSwapRequest>(r => pending.Contains(r.Status) && (r.RequesterShiftAssignmentId == id || r.TargetShiftAssignmentId == id), ct))
            {
                swap.Status = SwapStatus.Cancelled; swap.CancelledByUserId = current.UserId; swap.CancelledAt = now;
                swap.CancellationReason = "The Manager cancelled an affected shift.";
                await store.Update(swap, ct);
                await notifier.Broadcast(ShiftSwaps.Cancelled, "Shift swap cancelled", $"The Manager cancelled {Describe(slot)}, so the swap request was cancelled.",
                    new[] { swap.RequestedByUserId, swap.TargetUserId }.OfType<Guid>(), "SWAP_PARTICIPANT", ct);
            }
            if (wasPublished)
            {
                var reason = string.IsNullOrWhiteSpace(request.Reason) ? "" : $" Reason: {request.Reason.Trim()}";
                await notifier.Broadcast(Removed, "Shift cancelled", $"Your shift {Describe(slot)} was cancelled.{reason}", [row.UserId], "SHIFT_ASSIGNEE", ct);
            }
            return 0;
        }, ct);
        return await View(id, ct);
    }

    /// <summary>Publishes every future DRAFT assignment in the range; refused while any Zone is below its minimum (BR-02).</summary>
    public async Task<PublishScheduleResult> Publish(PublishScheduleRequest request, CancellationToken ct)
    {
        UseCase.Manager(current);
        await schema.Require(ct);
        ShiftRules.Range(request.From, request.To);
        return await store.Transaction(async () =>
        {
            var now = clock.UtcNow;
            var context = await planner.Load(request.From, request.To, ct);
            var zones = await ActiveZones(context, ct);
            var gaps = Coverage(context, zones, request.From, request.To)
                .Where(c => c.BelowMinimum && calendar.Window(c.WorkDate, context.Shifts[c.ShiftId].StartTime, context.Shifts[c.ShiftId].EndTime).StartsAt > now).ToList();
            if (gaps.Count > 0)
                throw new ApplicationException("COVERAGE_GAPS", $"{gaps.Count} Zone shift(s) are below the minimum staff: "
                    + string.Join("; ", gaps.Take(5).Select(g => $"{g.ZoneName} · {g.ShiftName} {g.WorkDate:dd/MM} {g.Planned}/{g.MinStaff}")) + (gaps.Count > 5 ? "; …" : "."));
            var drafts = context.Assignments.Where(a => a.Status == ShiftAssignmentStatus.Draft && a.WorkDate >= request.From && a.WorkDate <= request.To)
                .Select(a => planner.Slot(context, a)).OfType<SwapSlot>().Where(s => s.StartsAt > now).ToList();
            foreach (var slot in drafts)
            {
                slot.Assignment.Status = ShiftAssignmentStatus.Scheduled;
                await store.Update(slot.Assignment, ct);
            }
            var byUser = drafts.GroupBy(s => s.Assignment.UserId).ToList();
            foreach (var group in byUser)
                await notifier.Broadcast(Published, "Schedule published",
                    $"{group.Count()} new shift(s) from {request.From:dd/MM} to {request.To:dd/MM}, first: {Describe(group.OrderBy(s => s.StartsAt).First())}.", [group.Key], "SHIFT_ASSIGNEE", ct);
            return new PublishScheduleResult(request.From, request.To, drafts.Count, byUser.Count);
        }, ct);
    }

    public async Task<UnavailabilityView[]> Unavailability(DateOnly? from, DateOnly? to, Guid? userId, CancellationToken ct)
    {
        UseCase.Manager(current);
        await schema.Require(ct);
        var today = calendar.Today(clock.UtcNow);
        var start = from ?? today; var end = to ?? start.AddDays(30);
        ShiftRules.Range(start, end);
        var fromUtc = calendar.DayStart(start); var toUtc = calendar.DayStart(end.AddDays(1));
        var rows = await store.List<StaffUnavailability>(u => u.EndAt > fromUtc && u.StartAt < toUtc && (userId == null || u.UserId == userId), ct);
        var ids = rows.Select(r => r.UserId).Distinct().ToList();
        var names = (await store.List<UserAccount>(u => ids.Contains(u.UserId), ct)).ToDictionary(u => u.UserId, u => u.FullName);
        return rows.OrderBy(r => r.StartAt).Select(r => new UnavailabilityView(r.UnavailabilityId, r.UserId, names.GetValueOrDefault(r.UserId) ?? "", r.StartAt, r.EndAt, r.Note, r.CreatedAt)).ToArray();
    }

    /// <summary>Approved leave (BR-35). Refused while the person still has live assignments in that period, so the Manager reassigns them first.</summary>
    public async Task<UnavailabilityView> AddUnavailability(UnavailabilityRequest request, CancellationToken ct)
    {
        UseCase.Manager(current);
        await schema.Require(ct);
        var start = DateTime.SpecifyKind(request.StartAt.ToUniversalTime(), DateTimeKind.Utc); var end = DateTime.SpecifyKind(request.EndAt.ToUniversalTime(), DateTimeKind.Utc);
        Rules.Require(end > start, "INVALID_RANGE", "The end must be after the start.");
        Rules.Require(end > clock.UtcNow, "INVALID_RANGE", "Leave must end in the future.");
        Rules.Optional(request.Note, 1000, "Note");
        var id = await store.Transaction(async () =>
        {
            var from = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(start, calendar.TimeZone));
            var to = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(end, calendar.TimeZone));
            var context = await planner.Load(from, to, ct);
            var user = context.Staff.GetValueOrDefault(request.UserId) ?? throw new ApplicationException("NOT_STAFF", "Only active Staff accounts can have leave recorded.", 422);
            var clashes = context.Assignments.Where(a => a.UserId == user.UserId).Select(a => planner.Slot(context, a)).OfType<SwapSlot>()
                .Where(s => s.StartsAt < end && start < s.EndsAt && s.EndsAt > clock.UtcNow).OrderBy(s => s.StartsAt).ToList();
            if (clashes.Count > 0)
                throw new ApplicationException("LEAVE_CONFLICTS_SCHEDULE", $"{user.FullName} is scheduled during this leave: "
                    + string.Join("; ", clashes.Select(Describe)) + ". Reassign or cancel those shifts first.");
            var row = new StaffUnavailability { UnavailabilityId = Guid.NewGuid(), UserId = user.UserId, StartAt = start, EndAt = end,
                Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(), CreatedAt = clock.UtcNow };
            await store.Add(row, ct);
            return row.UnavailabilityId;
        }, ct);
        return (await Unavailability(DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(start, calendar.TimeZone)), null, request.UserId, ct)).Single(v => v.UnavailabilityId == id);
    }

    public async Task RemoveUnavailability(Guid id, CancellationToken ct)
    {
        UseCase.Manager(current);
        await schema.Require(ct);
        await store.Transaction(async () =>
        {
            await store.Remove(UseCase.Found(await store.Find<StaffUnavailability>(id, ct)), ct);
            return 0;
        }, ct);
    }

    private static WorkShiftView WorkShift(Shift s, bool inUse) => new(s.ShiftId, s.WeekStart, s.Name, s.StartTime, s.EndTime, ShiftRules.Hours(s.StartTime, s.EndTime), s.Status, inUse);
    private async Task<ScheduledShiftView> View(Guid id, CancellationToken ct)
    {
        var row = UseCase.Found(await store.Find<ShiftAssignment>(id, ct));
        // Cancelled rows are not in the schedule range read, so build the view from the row itself.
        var schedule = await Schedule(row.WorkDate, row.WorkDate, ct);
        return schedule.Assignments.SingleOrDefault(a => a.ShiftAssignmentId == id) ?? await CancelledView(row, ct);
    }
    private async Task<ScheduledShiftView> CancelledView(ShiftAssignment row, CancellationToken ct)
    {
        var shift = UseCase.Found(await store.Find<Shift>(row.ShiftId, ct));
        var (startsAt, endsAt) = calendar.Window(row.WorkDate, shift.StartTime, shift.EndTime);
        var name = (await store.Find<UserAccount>(row.UserId, ct))?.FullName ?? "";
        return new(row.ShiftAssignmentId, row.UserId, name, shift.ShiftId, shift.Name, row.WorkDate, startsAt, endsAt, row.Status, row.CheckInAt, [], false, null, row.UpdatedAt);
    }
    private static bool Editable(SwapSlot slot, DateTime now, bool throwIfNot = false)
    {
        var editable = slot.Assignment.Status is ShiftAssignmentStatus.Draft or ShiftAssignmentStatus.Scheduled && slot.StartsAt > now && slot.Assignment.CheckInAt is null;
        if (!editable && throwIfNot)
            throw new ApplicationException("SHIFT_LOCKED", "Only a draft or published shift that has not started can be changed (BR-35).");
        return editable;
    }
    private async Task Changeable(SwapSlot slot, CancellationToken ct)
    {
        Editable(slot, clock.UtcNow, throwIfNot: true);
        var id = slot.Assignment.ShiftAssignmentId; var now = clock.UtcNow; var pending = SwapStatus.Pending;
        if ((await store.List<ShiftSwapRequest>(r => pending.Contains(r.Status) && (r.ExpiresAt == null || r.ExpiresAt > now)
                && (r.RequesterShiftAssignmentId == id || r.TargetShiftAssignmentId == id), ct)).Count > 0)
            throw new ApplicationException("SWAP_PENDING", "A shift swap request is pending on this shift; wait for it to be decided or cancel the shift.");
    }
    private void Future(SwapSlot slot)
    {
        if (slot.StartsAt <= clock.UtcNow) throw new ApplicationException("SHIFT_LOCKED", "A shift that has started cannot be scheduled or changed (BR-35).");
    }
    private static void Allowed(List<SwapCheckView> checks)
    {
        var failed = checks.Where(c => !c.Passed).ToList();
        if (failed.Count > 0) throw new ApplicationException("SCHEDULE_CONFLICT", string.Join(" ", failed.Select(c => c.Message)));
    }
    private static void RequireWorkShiftWeek(Shift shift, DateOnly workDate)
    {
        if (shift.WeekStart != ShiftRules.WeekStart(workDate))
            throw new ApplicationException("WORK_SHIFT_WEEK_MISMATCH", "Choose a Work Shift configured for the assignment week.", 422);
    }
    private static Guid[] ZoneList(Guid[]? zoneIds)
    {
        var ids = (zoneIds ?? []).Where(z => z != Guid.Empty).Distinct().ToArray();
        Rules.Require(ids.Length > 0, "ZONE_REQUIRED", "Assign one Zone.");
        if (ids.Length > 1)
            throw new ApplicationException("MULTIPLE_ZONES_NOT_ALLOWED", "Assign exactly one Zone to each Staff shift.", 422);
        return ids;
    }
    private async Task RequireZones(Guid[] zoneIds, CancellationToken ct)
    {
        var found = await store.List<Zone>(z => zoneIds.Contains(z.ZoneId) && z.Status == "ACTIVE", ct);
        if (found.Count != zoneIds.Length) throw new ApplicationException("ZONE_NOT_FOUND", "Every Zone must exist and be active.", 422);
    }
    /// <summary>Keeps the selected Zone row, ends other active rows and adds the selected Zone when needed.</summary>
    private async Task SetZones(Guid assignmentId, Guid[] zoneIds, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var active = await store.List<StaffZoneAssignment>(z => z.ShiftAssignmentId == assignmentId && z.UnassignedAt == null, ct);
        foreach (var row in active.Where(z => !zoneIds.Contains(z.ZoneId)))
        {
            row.UnassignedAt = now; row.Status = "INACTIVE";
            await store.Update(row, ct);
        }
        foreach (var zoneId in zoneIds.Where(z => active.All(a => a.ZoneId != z)))
            await store.Add(new StaffZoneAssignment { StaffZoneAssignmentId = Guid.NewGuid(), ShiftAssignmentId = assignmentId, ZoneId = zoneId, Status = "ACTIVE", AssignedAt = now }, ct);
    }
    private async Task<Dictionary<Guid, Zone>> ActiveZones(SwapContext context, CancellationToken ct)
    {
        var zones = (await store.List<Zone>(z => z.Status == "ACTIVE", ct)).ToDictionary(z => z.ZoneId);
        foreach (var (id, zone) in context.Zones) zones.TryAdd(id, zone);
        return zones;
    }
    private ZoneCoverageView[] Coverage(SwapContext context, Dictionary<Guid, Zone> zones, DateOnly from, DateOnly to)
    {
        var rows = context.Assignments.Where(a => a.WorkDate >= from && a.WorkDate <= to).ToList();
        var used = rows.Select(a => a.ShiftId).ToHashSet();
        var result = new List<ZoneCoverageView>();
        for (var day = from; day <= to; day = day.AddDays(1))
            foreach (var shift in context.Shifts.Values
                .Where(s => s.WeekStart == ShiftRules.WeekStart(day) && (s.Status == WorkShiftStatus.Active || used.Contains(s.ShiftId)))
                .OrderBy(s => s.StartTime))
                foreach (var zone in zones.Values.Where(z => z.Status == "ACTIVE").OrderBy(z => z.Code))
                {
                    var here = rows.Where(a => a.ShiftId == shift.ShiftId && a.WorkDate == day && SwapPlanner.ZoneIds(context, a.ShiftAssignmentId).Contains(zone.ZoneId)).ToList();
                    if (zone.MinStaff == 0 && here.Count == 0) continue;
                    result.Add(new(day, shift.ShiftId, shift.Name, zone.ZoneId, zone.Name, zone.MinStaff, here.Count,
                        here.Count(a => a.Status == ShiftAssignmentStatus.Scheduled), here.Count < zone.MinStaff));
                }
        return result.ToArray();
    }
    private string Describe(SwapSlot slot)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(slot.StartsAt, calendar.TimeZone);
        return $"{slot.Shift.Name} {slot.Assignment.WorkDate:ddd dd/MM} {local:HH:mm}";
    }
}
