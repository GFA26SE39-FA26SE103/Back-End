using Supermarket.Domain;
namespace Supermarket.Application;

/// <summary>Required ERD V6 tables and columns for SWAP/COVER requests.</summary>
public interface IShiftSwapSchema
{
    Task<bool> Ready(CancellationToken ct);
}
public static class ShiftSwapSchemaCheck
{
    public static async Task Require(this IShiftSwapSchema schema, CancellationToken ct)
    {
        if (!await schema.Ready(ct))
            throw new ApplicationException("SWAP_SCHEMA_NOT_READY", "Apply the approved FA26SE103 ERD V6 database migration before using shift swaps.", 503);
    }
}

public sealed record SwapSlot(ShiftAssignment Assignment, Shift Shift, DateTime StartsAt, DateTime EndsAt)
{
    public double Hours => (EndsAt - StartsAt).TotalHours;
}
/// <summary>The change being asked for: SWAP exchanges both slots, COVER gives the requester's slot to the target.</summary>
public sealed record SwapPlan(string Type, Guid RequesterId, SwapSlot RequesterSlot, Guid TargetId, SwapSlot? TargetSlot);

/// <summary>Schedule data around the affected dates, loaded once so every rule is evaluated in memory.</summary>
public sealed class SwapContext
{
    public required Dictionary<Guid, Shift> Shifts { get; init; }
    public required List<ShiftAssignment> Assignments { get; init; }
    public required List<StaffUnavailability> Unavailable { get; init; }
    public required List<ShiftSwapRequest> Pending { get; init; }
    public required Dictionary<Guid, UserAccount> Staff { get; init; }
    public required List<StaffZoneAssignment> ZoneRows { get; init; }
    public required Dictionary<Guid, Zone> Zones { get; init; }
}

/// <summary>Evaluates the SWAP/COVER rules: future shifts, decision deadline, one open request per shift, no overlap,
/// availability, the weekly hour limit and Zone coverage. The same rules run on request, peer acceptance and Manager approval.</summary>
public sealed class SwapPlanner(ISetupStore store, StoreCalendar calendar)
{
    public async Task<SwapContext> Load(DateOnly from, DateOnly to, CancellationToken ct)
    {
        // Weekly hours need the whole local week; a night shift may start the previous day.
        var start = from.AddDays(-8); var end = to.AddDays(8);
        var assignments = await store.List<ShiftAssignment>(a => a.WorkDate >= start && a.WorkDate <= end && a.Status != "CANCELLED", ct);
        var shifts = (await store.List<Shift>(null, ct)).ToDictionary(s => s.ShiftId);
        var fromUtc = calendar.DayStart(start); var toUtc = calendar.DayStart(end.AddDays(1));
        var unavailable = await store.List<StaffUnavailability>(u => u.EndAt > fromUtc && u.StartAt < toUtc, ct);
        var pending = SwapStatus.Pending;
        var requests = await store.List<ShiftSwapRequest>(r => pending.Contains(r.Status), ct);
        var staffRole = (await store.List<Role>(r => r.Name == "STAFF", ct)).SingleOrDefault();
        var staff = staffRole is null ? [] : (await store.List<UserAccount>(u => u.RoleId == staffRole.RoleId && u.Status == "ACTIVE", ct)).ToDictionary(u => u.UserId);
        var ids = assignments.Select(a => a.ShiftAssignmentId).ToList();
        var zoneRows = await store.List<StaffZoneAssignment>(z => ids.Contains(z.ShiftAssignmentId) && z.UnassignedAt == null && z.Status == "ACTIVE", ct);
        var zoneIds = zoneRows.Select(z => z.ZoneId).Distinct().ToList();
        var zones = (await store.List<Zone>(z => zoneIds.Contains(z.ZoneId), ct)).ToDictionary(z => z.ZoneId);
        return new() { Shifts = shifts, Assignments = assignments, Unavailable = unavailable, Pending = requests, Staff = staff, ZoneRows = zoneRows, Zones = zones };
    }

    public SwapSlot? Slot(SwapContext context, ShiftAssignment? assignment)
    {
        if (assignment is null || !context.Shifts.TryGetValue(assignment.ShiftId, out var shift)) return null;
        var (startsAt, endsAt) = calendar.Window(assignment.WorkDate, shift.StartTime, shift.EndTime);
        return new(assignment, shift, startsAt, endsAt);
    }
    public SwapSlot? Slot(SwapContext context, Guid assignmentId) => Slot(context, context.Assignments.SingleOrDefault(a => a.ShiftAssignmentId == assignmentId));

    public static DateTime Deadline(SwapPlan plan) => SwapRules.Deadline(plan.RequesterSlot.StartsAt, plan.TargetSlot?.StartsAt);

    public List<SwapCheckView> Checks(SwapContext context, SwapPlan plan, DateTime now, Guid? ignoreRequestId = null)
    {
        var checks = new List<SwapCheckView>();
        void Add(string code, bool passed, string message) => checks.Add(new(code, passed, message));
        var requester = Name(context, plan.RequesterId); var target = Name(context, plan.TargetId);

        Add("ACTIVE_STAFF", context.Staff.ContainsKey(plan.RequesterId) && context.Staff.ContainsKey(plan.TargetId) && plan.RequesterId != plan.TargetId,
            "Both people must be different, active Staff members.");
        var slots = new[] { plan.RequesterSlot, plan.TargetSlot }.OfType<SwapSlot>().ToArray();
        Add("FUTURE_SHIFT", slots.All(s => s.StartsAt > now && s.Assignment.CheckInAt is null && s.Assignment.Status == ShiftAssignmentStatus.Scheduled),
            "Every affected shift must be published, still in the future and not checked in.");
        var deadline = Deadline(plan);
        Add("DECISION_DEADLINE", now < deadline, $"Requests must be settled 24 hours before the earliest affected shift ({deadline:O}).");
        if (plan.TargetSlot is { } same)
            Add("DIFFERENT_SHIFT", same.Assignment.ShiftId != plan.RequesterSlot.Assignment.ShiftId || same.Assignment.WorkDate != plan.RequesterSlot.Assignment.WorkDate,
                "A swap must exchange two different shifts.");
        var touched = slots.Select(s => s.Assignment.ShiftAssignmentId).ToHashSet();
        Add("ONE_OPEN_REQUEST", !context.Pending.Any(r => r.SwapRequestId != ignoreRequestId && (r.ExpiresAt is null || r.ExpiresAt > now)
                && (touched.Contains(r.RequesterShiftAssignmentId) || r.TargetShiftAssignmentId is { } t && touched.Contains(t))),
            "Each shift can have only one pending swap or cover request.");

        // The target takes the requester's shift; for SWAP the requester also takes the target's shift.
        checks.AddRange(PersonChecks(context, target, plan.TargetId, plan.RequesterSlot, plan.TargetSlot));
        if (plan.TargetSlot is { } targetSlot) checks.AddRange(PersonChecks(context, requester, plan.RequesterId, targetSlot, plan.RequesterSlot));

        // Zone rows move with the assignment, so each Zone keeps the same number of Staff for the shift.
        foreach (var slot in slots)
            foreach (var zoneId in ZoneIds(context, slot.Assignment.ShiftAssignmentId))
            {
                var zone = context.Zones.GetValueOrDefault(zoneId);
                var staffed = context.Assignments.Count(a => a.ShiftId == slot.Assignment.ShiftId && a.WorkDate == slot.Assignment.WorkDate
                    && ZoneIds(context, a.ShiftAssignmentId).Contains(zoneId));
                Add("ZONE_COVERAGE", true, $"{zone?.Name ?? "Zone"} keeps {staffed} Staff on {slot.Shift.Name} {slot.Assignment.WorkDate:yyyy-MM-dd} (minimum {zone?.MinStaff ?? 0}).");
            }
        return checks;
    }

    /// <summary>Hours the person would work in the local week of <paramref name="gains"/> after the change.</summary>
    public double WeeklyHours(SwapContext context, Guid userId, SwapSlot gains, SwapSlot? loses)
    {
        var monday = gains.Assignment.WorkDate.AddDays(-(((int)gains.Assignment.WorkDate.DayOfWeek + 6) % 7));
        var sunday = monday.AddDays(6);
        var hours = context.Assignments.Where(a => a.UserId == userId && a.WorkDate >= monday && a.WorkDate <= sunday
                && a.ShiftAssignmentId != loses?.Assignment.ShiftAssignmentId && a.ShiftAssignmentId != gains.Assignment.ShiftAssignmentId)
            .Select(a => Slot(context, a)).OfType<SwapSlot>().Sum(s => s.Hours);
        return Math.Round(hours + gains.Hours, 2);
    }

    /// <summary>BR-34/BR-35 for one person taking <paramref name="gains"/> (and giving up <paramref name="loses"/>): no overlap, not on leave, weekly limit.</summary>
    public List<SwapCheckView> PersonChecks(SwapContext context, string name, Guid userId, SwapSlot gains, SwapSlot? loses)
    {
        var checks = new List<SwapCheckView>();
        var overlapping = context.Assignments.Where(a => a.UserId == userId && a.ShiftAssignmentId != loses?.Assignment.ShiftAssignmentId
                && a.ShiftAssignmentId != gains.Assignment.ShiftAssignmentId)
            .Select(a => Slot(context, a)).OfType<SwapSlot>().Any(s => s.StartsAt < gains.EndsAt && gains.StartsAt < s.EndsAt);
        checks.Add(new("NO_OVERLAP", !overlapping, $"{name} has no other shift overlapping {gains.Shift.Name} {gains.Assignment.WorkDate:yyyy-MM-dd}."));
        var unavailable = context.Unavailable.Any(u => u.UserId == userId && u.StartAt < gains.EndsAt && gains.StartsAt < u.EndAt);
        checks.Add(new("AVAILABLE", !unavailable, $"{name} is not marked unavailable during {gains.Shift.Name} {gains.Assignment.WorkDate:yyyy-MM-dd}."));
        var hours = WeeklyHours(context, userId, gains, loses);
        checks.Add(new("WEEKLY_HOURS", hours <= SwapRules.MaxWeeklyHours, $"{name} would work {hours:0.##}/{SwapRules.MaxWeeklyHours:0} hours that week."));
        return checks;
    }

    public static List<Guid> ZoneIds(SwapContext context, Guid assignmentId)
        => context.ZoneRows.Where(z => z.ShiftAssignmentId == assignmentId).Select(z => z.ZoneId).Distinct().ToList();
    private static string Name(SwapContext context, Guid userId) => context.Staff.GetValueOrDefault(userId)?.FullName ?? "The Staff member";
}

/// <summary>
/// SWAP/COVER lifecycle: Staff A requests → Staff B accepts or declines → a Manager approves or rejects.
/// A cancels while pending; B withdraws while the Manager decides. Pending requests expire 24 hours before the earliest affected shift.
/// Only approval changes ShiftAssignment rows, atomically and after re-checking every rule.
/// </summary>
public sealed class ShiftSwaps(ISetupStore store, ICurrentUser current, IClock clock, StoreCalendar calendar, SwapPlanner planner, TaskNotifier notifier, IShiftSwapSchema schema)
{
    public const string Received = "SWAP_REQUEST_RECEIVED", PeerAccepted = "SWAP_PEER_ACCEPTED", PeerDeclined = "SWAP_PEER_DECLINED",
        PendingApproval = "SWAP_PENDING_APPROVAL", Cancelled = "SWAP_CANCELLED", Approved = "SWAP_APPROVED", Rejected = "SWAP_REJECTED", Expired = "SWAP_EXPIRED";

    /// <summary>Colleagues who could take the caller's shift (COVER) or exchange one of their shifts within a week of it (SWAP).</summary>
    public async Task<SwapCandidatesView> Candidates(Guid shiftAssignmentId, string? type, CancellationToken ct)
    {
        UseCase.Staff(current);
        await schema.Require(ct);
        var kind = type ?? SwapType.Swap;
        Rules.Require(kind is SwapType.Swap or SwapType.Cover, "INVALID_SWAP_TYPE", "The request type must be SWAP or COVER.");
        var mine = await store.Find<ShiftAssignment>(shiftAssignmentId, ct);
        if (mine is null || mine.UserId != current.UserId) throw new ApplicationException("NOT_FOUND", "The resource was not found.", 404);
        var context = await planner.Load(mine.WorkDate.AddDays(-7), mine.WorkDate.AddDays(7), ct);
        var mySlot = UseCase.Found(planner.Slot(context, mine));
        var now = clock.UtcNow;
        var candidates = new List<SwapCandidateView>(); var hidden = 0;
        foreach (var colleague in context.Staff.Values.Where(u => u.UserId != current.UserId).OrderBy(u => u.FullName))
        {
            SwapSlot?[] options = kind == SwapType.Cover ? [null]
                : context.Assignments.Where(a => a.UserId == colleague.UserId && a.Status == ShiftAssignmentStatus.Scheduled && a.CheckInAt == null && Math.Abs(a.WorkDate.DayNumber - mine.WorkDate.DayNumber) <= 7)
                    .Select(a => planner.Slot(context, a)).OfType<SwapSlot>().Where(s => s.StartsAt > now).OrderBy(s => s.StartsAt).ToArray<SwapSlot?>();
            var found = false;
            foreach (var option in options)
            {
                var plan = new SwapPlan(kind, current.UserId, mySlot, colleague.UserId, option);
                if (planner.Checks(context, plan, now).Any(c => !c.Passed)) continue;
                found = true;
                candidates.Add(new(new(colleague.UserId, colleague.FullName), option is null ? null : ShiftView(context, option),
                    planner.WeeklyHours(context, colleague.UserId, mySlot, option)));
            }
            if (!found) hidden++;
        }
        return new(shiftAssignmentId, kind, SwapRules.Deadline(mySlot.StartsAt, null), candidates.ToArray(), hidden);
    }

    public async Task<SwapRequestView> Create(SwapCreateRequest request, CancellationToken ct)
    {
        UseCase.Staff(current);
        await schema.Require(ct);
        SwapRules.Request(request.Type, request.TargetUserId, request.TargetShiftAssignmentId, request.ReasonCode, request.Note);
        var id = Guid.NewGuid();
        await store.Transaction(async () =>
        {
            var mine = await store.Find<ShiftAssignment>(request.RequesterShiftAssignmentId, ct);
            if (mine is null || mine.UserId != current.UserId) throw new ApplicationException("NOT_FOUND", "The resource was not found.", 404);
            var theirs = request.TargetShiftAssignmentId is { } targetShiftId ? await store.Find<ShiftAssignment>(targetShiftId, ct) : null;
            if (request.TargetShiftAssignmentId is not null && (theirs is null || theirs.UserId != request.TargetUserId))
                throw new ApplicationException("NOT_FOUND", "The colleague's shift was not found.", 404);
            var dates = new[] { mine.WorkDate, theirs?.WorkDate ?? mine.WorkDate };
            var context = await planner.Load(dates.Min(), dates.Max(), ct);
            if (!context.Staff.ContainsKey(request.TargetUserId!.Value) || request.TargetUserId == current.UserId)
                throw new ApplicationException("TARGET_NOT_STAFF", "Choose another active Staff member.", 422);
            var plan = new SwapPlan(request.Type, current.UserId, UseCase.Found(planner.Slot(context, mine)), request.TargetUserId.Value, planner.Slot(context, theirs));
            var now = clock.UtcNow;
            Allowed(planner.Checks(context, plan, now));
            var swap = new ShiftSwapRequest { SwapRequestId = id, RequestType = request.Type, RequestedByUserId = current.UserId, RequesterShiftAssignmentId = mine.ShiftAssignmentId,
                TargetUserId = request.TargetUserId, TargetShiftAssignmentId = theirs?.ShiftAssignmentId, ReasonCode = request.ReasonCode, Note = Clean(request.Note),
                Status = SwapStatus.PendingPeer, ExpiresAt = SwapPlanner.Deadline(plan) };
            await store.Add(swap, ct);
            var kind = request.Type == SwapType.Swap ? "swap shifts" : "cover a shift";
            await Send(Received, "Shift swap request", $"{Name(context, current.UserId)} asks you to {kind}: {Describe(plan.RequesterSlot)}.", [plan.TargetId], "SWAP_TARGET", ct);
            return 0;
        }, ct);
        return await View(id, ct);
    }

    /// <summary>box = sent | received; both when omitted.</summary>
    public async Task<SwapRequestView[]> Mine(string? box, CancellationToken ct)
    {
        UseCase.Staff(current);
        await schema.Require(ct);
        Rules.Require(box is null or "sent" or "received", "INVALID_BOX", "box must be sent or received.");
        var me = current.UserId;
        var rows = await store.List<ShiftSwapRequest>(r => (box != "received" && r.RequestedByUserId == me) || (box != "sent" && r.TargetUserId == me), ct);
        return await Views(rows.OrderByDescending(r => r.CreatedAt).Take(100).ToList(), ct);
    }

    /// <summary>Manager queue: PENDING_MANAGER by default; a comma list of statuses or ALL.</summary>
    public async Task<SwapRequestView[]> ForManager(string? status, CancellationToken ct)
    {
        UseCase.Manager(current);
        await schema.Require(ct);
        string[] all = [.. SwapStatus.Pending, SwapStatus.DeclinedByPeer, SwapStatus.Approved, SwapStatus.Rejected, SwapStatus.Cancelled, SwapStatus.Expired];
        string[] statuses = status is null ? [SwapStatus.PendingManager] : string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase) ? all
            : status.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var s in statuses) Rules.Status(s, all);
        var rows = await store.List<ShiftSwapRequest>(r => statuses.Contains(r.Status), ct);
        return await Views(rows.OrderBy(r => r.ExpiresAt ?? r.CreatedAt).Take(200).ToList(), ct);
    }

    public async Task<SwapRequestView> Get(Guid id, CancellationToken ct)
    {
        await schema.Require(ct);
        var swap = UseCase.Found(await store.Find<ShiftSwapRequest>(id, ct));
        Visible(swap);
        return (await Views([swap], ct)).Single();
    }

    /// <summary>Staff B accepts (rules are re-checked, the request goes to the Manager) or declines.</summary>
    public async Task<SwapRequestView> Respond(Guid id, SwapRespondRequest request, CancellationToken ct)
    {
        UseCase.Staff(current);
        await schema.Require(ct);
        await Change(id, async (swap, now) =>
        {
            if (swap.TargetUserId != current.UserId) throw new ApplicationException("NOT_FOUND", "The resource was not found.", 404);
            Pending(swap, now, SwapStatus.PendingPeer);
            var (context, plan) = await Plan(swap, ct);
            swap.TargetRespondedAt = now;
            if (!request.Accept)
            {
                swap.Status = SwapStatus.DeclinedByPeer;
                await store.Update(swap, ct);
                await Send(PeerDeclined, "Swap request declined", $"{Name(context, current.UserId)} declined your request for {Describe(plan.RequesterSlot)}. You keep this shift.",
                    [swap.RequestedByUserId], "SWAP_REQUESTER", ct);
                return;
            }
            Allowed(planner.Checks(context, plan, now, swap.SwapRequestId));
            swap.Status = SwapStatus.PendingManager;
            await store.Update(swap, ct);
            await Send(PeerAccepted, "Colleague accepted your swap", $"{Name(context, current.UserId)} accepted. A Manager now reviews the request for {Describe(plan.RequesterSlot)}.",
                [swap.RequestedByUserId], "SWAP_REQUESTER", ct);
            await Send(PendingApproval, "Shift swap waiting for approval", $"{Name(context, swap.RequestedByUserId)} and {Name(context, current.UserId)}: {Describe(plan.RequesterSlot)}. Decide before {swap.ExpiresAt:O}.",
                await notifier.RoleUsers("MANAGER", ct), "MANAGER", ct);
        }, ct);
        return await View(id, ct);
    }

    /// <summary>The requester cancels while pending; the colleague withdraws an acceptance while the Manager has not decided.</summary>
    public async Task<SwapRequestView> Cancel(Guid id, SwapCancelRequest request, CancellationToken ct)
    {
        UseCase.Staff(current);
        await schema.Require(ct);
        Rules.Optional(request.Reason, 1000, "Reason");
        await Change(id, async (swap, now) =>
        {
            var me = current.UserId;
            if (swap.RequestedByUserId != me && swap.TargetUserId != me) throw new ApplicationException("NOT_FOUND", "The resource was not found.", 404);
            if (swap.RequestedByUserId == me) Pending(swap, now, SwapStatus.PendingPeer, SwapStatus.PendingManager);
            else Pending(swap, now, SwapStatus.PendingManager);
            var wasWithManager = swap.Status == SwapStatus.PendingManager;
            var (context, plan) = await Plan(swap, ct);
            swap.Status = SwapStatus.Cancelled; swap.CancelledByUserId = me; swap.CancelledAt = now; swap.CancellationReason = Clean(request.Reason);
            await store.Update(swap, ct);
            var other = swap.RequestedByUserId == me ? swap.TargetUserId!.Value : swap.RequestedByUserId;
            var recipients = new List<Guid> { other };
            if (wasWithManager) recipients.AddRange(await notifier.RoleUsers("MANAGER", ct));
            await Send(Cancelled, "Shift swap cancelled", $"{Name(context, me)} cancelled the request for {Describe(plan.RequesterSlot)}. Shifts stay as scheduled.", recipients, "SWAP_PARTICIPANT", ct);
        }, ct);
        return await View(id, ct);
    }

    public async Task<SwapRequestView> Approve(Guid id, SwapDecisionRequest request, CancellationToken ct)
    {
        UseCase.Manager(current);
        await schema.Require(ct);
        Rules.Optional(request.Note, 1000, "Note");
        await Change(id, async (swap, now) =>
        {
            Pending(swap, now, SwapStatus.PendingManager);
            var (context, plan) = await Plan(swap, ct);
            Allowed(planner.Checks(context, plan, now, swap.SwapRequestId));
            var requesterShift = plan.RequesterSlot.Assignment;
            requesterShift.UserId = plan.TargetId;
            await store.Update(requesterShift, ct);
            if (plan.TargetSlot is { } targetSlot)
            {
                targetSlot.Assignment.UserId = plan.RequesterId;
                await store.Update(targetSlot.Assignment, ct);
            }
            swap.Status = SwapStatus.Approved; swap.DecidedByUserId = current.UserId; swap.DecidedAt = now; swap.DecisionNote = Clean(request.Note);
            await store.Update(swap, ct);
            var change = plan.TargetSlot is { } given
                ? $"{Name(context, plan.RequesterId)} now works {Describe(given)}; {Name(context, plan.TargetId)} works {Describe(plan.RequesterSlot)}."
                : $"{Name(context, plan.TargetId)} now covers {Describe(plan.RequesterSlot)}.";
            await Send(Approved, "Shift swap approved", change, [plan.RequesterId, plan.TargetId], "SWAP_PARTICIPANT", ct);
        }, ct);
        return await View(id, ct);
    }

    public async Task<SwapRequestView> Reject(Guid id, SwapDecisionRequest request, CancellationToken ct)
    {
        UseCase.Manager(current);
        await schema.Require(ct);
        var note = Rules.Text(request.Note, 1000, "Rejection reason");
        await Change(id, async (swap, now) =>
        {
            Pending(swap, now, SwapStatus.PendingManager);
            var (context, plan) = await Plan(swap, ct);
            swap.Status = SwapStatus.Rejected; swap.DecidedByUserId = current.UserId; swap.DecidedAt = now; swap.DecisionNote = note;
            await store.Update(swap, ct);
            await Send(Rejected, "Shift swap rejected", $"Request for {Describe(plan.RequesterSlot)} was rejected: {note}. Shifts stay as scheduled.",
                [plan.RequesterId, plan.TargetId], "SWAP_PARTICIPANT", ct);
        }, ct);
        return await View(id, ct);
    }

    /// <summary>Background expiry: pending requests whose deadline has passed become EXPIRED; nothing in the schedule changes.</summary>
    public async Task<int> ExpireDue(CancellationToken ct)
    {
        if (!await schema.Ready(ct)) return 0;
        return await store.Transaction(async () =>
        {
            var now = clock.UtcNow; var pending = SwapStatus.Pending;
            var due = await store.List<ShiftSwapRequest>(r => pending.Contains(r.Status) && r.ExpiresAt != null && r.ExpiresAt <= now, ct);
            foreach (var swap in due)
            {
                var wasWithManager = swap.Status == SwapStatus.PendingManager;
                swap.Status = SwapStatus.Expired;
                await store.Update(swap, ct);
                var recipients = new List<Guid> { swap.RequestedByUserId };
                if (swap.TargetUserId is { } target) recipients.Add(target);
                if (wasWithManager) recipients.AddRange(await notifier.RoleUsers("MANAGER", ct));
                await Send(Expired, "Shift swap expired", "The request was not settled 24 hours before the shift. Shifts stay as scheduled.", recipients, "SWAP_PARTICIPANT", ct);
            }
            return due.Count;
        }, ct);
    }

    private async Task Change(Guid id, Func<ShiftSwapRequest, DateTime, Task> work, CancellationToken ct)
        => await store.Transaction(async () =>
        {
            var swap = UseCase.Found(await store.Find<ShiftSwapRequest>(id, ct));
            await work(swap, clock.UtcNow);
            return 0;
        }, ct);

    private static void Pending(ShiftSwapRequest swap, DateTime now, params string[] allowed)
    {
        if (SwapStatus.Pending.Contains(swap.Status) && swap.ExpiresAt is { } expires && now >= expires)
            throw new ApplicationException("SWAP_EXPIRED", "The request expired 24 hours before the shift and can no longer change.");
        if (!allowed.Contains(swap.Status))
            throw new ApplicationException("SWAP_NOT_PENDING", $"The request is {swap.Status} and cannot be changed this way.");
    }
    private static void Allowed(List<SwapCheckView> checks)
    {
        var failed = checks.Where(c => !c.Passed).ToList();
        if (failed.Count > 0) throw new ApplicationException("SWAP_NOT_ALLOWED", string.Join(" ", failed.Select(c => c.Message)));
    }
    private void Visible(ShiftSwapRequest swap)
    {
        if (current.Role == "MANAGER") return;
        if (current.Role == "STAFF" && (swap.RequestedByUserId == current.UserId || swap.TargetUserId == current.UserId)) return;
        throw new ApplicationException("NOT_FOUND", "The resource was not found.", 404);
    }
    private async Task<(SwapContext Context, SwapPlan Plan)> Plan(ShiftSwapRequest swap, CancellationToken ct)
    {
        var ids = new[] { swap.RequesterShiftAssignmentId, swap.TargetShiftAssignmentId }.OfType<Guid>().ToList();
        var rows = await store.List<ShiftAssignment>(a => ids.Contains(a.ShiftAssignmentId), ct);
        var context = await planner.Load(rows.Min(a => a.WorkDate), rows.Max(a => a.WorkDate), ct);
        // Cancelled assignments are not in the context; keep them so the rules can report them.
        foreach (var row in rows.Where(r => context.Assignments.All(a => a.ShiftAssignmentId != r.ShiftAssignmentId))) context.Assignments.Add(row);
        var requesterSlot = UseCase.Found(planner.Slot(context, swap.RequesterShiftAssignmentId));
        var targetSlot = swap.TargetShiftAssignmentId is { } targetId ? UseCase.Found(planner.Slot(context, targetId)) : null;
        return (context, new SwapPlan(swap.RequestType, swap.RequestedByUserId, requesterSlot, swap.TargetUserId ?? Guid.Empty, targetSlot));
    }
    private Task Send(string type, string title, string message, IEnumerable<Guid> recipients, string reason, CancellationToken ct)
        => notifier.Broadcast(type, title, message, recipients, reason, ct);

    private async Task<SwapRequestView> View(Guid id, CancellationToken ct) => (await Views([UseCase.Found(await store.Find<ShiftSwapRequest>(id, ct))], ct)).Single();
    private async Task<SwapRequestView[]> Views(List<ShiftSwapRequest> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var assignmentIds = rows.SelectMany(r => new[] { r.RequesterShiftAssignmentId, r.TargetShiftAssignmentId }).OfType<Guid>().Distinct().ToList();
        var assignments = await store.List<ShiftAssignment>(a => assignmentIds.Contains(a.ShiftAssignmentId), ct);
        var userIds = rows.SelectMany(r => new[] { r.RequestedByUserId, r.TargetUserId, r.DecidedByUserId, r.CancelledByUserId }).OfType<Guid>().Distinct().ToList();
        var users = (await store.List<UserAccount>(u => userIds.Contains(u.UserId), ct)).ToDictionary(u => u.UserId);
        var now = clock.UtcNow;
        var open = rows.Where(r => SwapStatus.Pending.Contains(r.Status)).ToList();
        // Rule details include colleagues' hours and availability, so only the Manager deciding the request sees them.
        SwapContext? context = null;
        if (open.Count > 0 && current.Role == "MANAGER")
        {
            var dates = assignments.Where(a => open.Any(r => r.RequesterShiftAssignmentId == a.ShiftAssignmentId || r.TargetShiftAssignmentId == a.ShiftAssignmentId)).Select(a => a.WorkDate).ToList();
            if (dates.Count > 0) context = await planner.Load(dates.Min(), dates.Max(), ct);
        }
        var shiftContext = context ?? new SwapContext { Shifts = (await store.List<Shift>(null, ct)).ToDictionary(s => s.ShiftId), Assignments = [], Unavailable = [], Pending = [],
            Staff = [], ZoneRows = [], Zones = [] };
        var zoneRows = await store.List<StaffZoneAssignment>(z => assignmentIds.Contains(z.ShiftAssignmentId) && z.UnassignedAt == null && z.Status == "ACTIVE", ct);
        var zoneIds = zoneRows.Select(z => z.ZoneId).Distinct().ToList();
        var zones = (await store.List<Zone>(z => zoneIds.Contains(z.ZoneId), ct)).ToDictionary(z => z.ZoneId);
        SwapShiftView? Shift(Guid? id)
        {
            var assignment = assignments.SingleOrDefault(a => a.ShiftAssignmentId == id);
            if (assignment is null || planner.Slot(shiftContext, assignment) is not { } slot) return null;
            return new(assignment.ShiftAssignmentId, assignment.UserId, slot.Shift.Name, assignment.WorkDate, slot.StartsAt, slot.EndsAt,
                zoneRows.Where(z => z.ShiftAssignmentId == assignment.ShiftAssignmentId && zones.ContainsKey(z.ZoneId)).Select(z => new ShiftZoneView(z.ZoneId, zones[z.ZoneId].Name)).DistinctBy(z => z.ZoneId).ToArray());
        }
        SwapPersonView? Person(Guid? id) => id is { } value ? new(value, users.GetValueOrDefault(value)?.FullName ?? "") : null;
        return rows.Select(r =>
        {
            var expired = SwapStatus.Pending.Contains(r.Status) && r.ExpiresAt is { } expires && now >= expires;
            var status = expired ? SwapStatus.Expired : r.Status;
            var role = r.RequestedByUserId == current.UserId ? "REQUESTER" : r.TargetUserId == current.UserId ? "TARGET" : current.Role;
            var checks = Array.Empty<SwapCheckView>();
            if (!expired && context is not null && SwapStatus.Pending.Contains(r.Status))
            {
                var requesterSlot = planner.Slot(context, r.RequesterShiftAssignmentId);
                var targetSlot = r.TargetShiftAssignmentId is { } t ? planner.Slot(context, t) : null;
                if (requesterSlot is not null && (r.TargetShiftAssignmentId is null || targetSlot is not null))
                    checks = planner.Checks(context, new SwapPlan(r.RequestType, r.RequestedByUserId, requesterSlot, r.TargetUserId ?? Guid.Empty, targetSlot), now, r.SwapRequestId).ToArray();
            }
            return new SwapRequestView(r.SwapRequestId, r.RequestType, status, Person(r.RequestedByUserId)!, Shift(r.RequesterShiftAssignmentId)!,
                Person(r.TargetUserId), Shift(r.TargetShiftAssignmentId), r.ReasonCode, r.Note, r.CreatedAt, r.ExpiresAt, r.TargetRespondedAt,
                Person(r.DecidedByUserId), r.DecidedAt, r.DecisionNote, Person(r.CancelledByUserId), r.CancelledAt, r.CancellationReason, role,
                CanCancel: !expired && ((role == "REQUESTER" && SwapStatus.Pending.Contains(r.Status)) || (role == "TARGET" && r.Status == SwapStatus.PendingManager)),
                CanRespond: !expired && role == "TARGET" && r.Status == SwapStatus.PendingPeer,
                CanDecide: !expired && role == "MANAGER" && r.Status == SwapStatus.PendingManager,
                checks, r.UpdatedAt);
        }).ToArray();
    }
    private SwapShiftView ShiftView(SwapContext context, SwapSlot slot)
        => new(slot.Assignment.ShiftAssignmentId, slot.Assignment.UserId, slot.Shift.Name, slot.Assignment.WorkDate, slot.StartsAt, slot.EndsAt,
            SwapPlanner.ZoneIds(context, slot.Assignment.ShiftAssignmentId).Where(context.Zones.ContainsKey).Select(z => new ShiftZoneView(z, context.Zones[z].Name)).ToArray());
    private string Describe(SwapSlot slot)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(slot.StartsAt, calendar.TimeZone);
        return $"{slot.Shift.Name} {slot.Assignment.WorkDate:ddd dd/MM} {local:HH:mm}";
    }
    private static string Name(SwapContext context, Guid userId) => context.Staff.GetValueOrDefault(userId)?.FullName ?? "A colleague";
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
