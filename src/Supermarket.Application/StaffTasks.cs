using System.Text.Json;
using Supermarket.Domain;
namespace Supermarket.Application;

/// <summary>Staff mobile use cases: own shifts and check-in, task offers and responses, own tasks and notifications.</summary>
public sealed class StaffTasks(ISetupStore store, ICurrentUser current, IClock clock, StoreCalendar calendar, StaffRoster roster, TaskDispatch dispatch, TaskViews views, ITaskSchema schema)
{
    public async Task<ShiftView[]> Shifts(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        UseCase.Staff(current);
        await schema.Require(ct);
        var today = calendar.Today(clock.UtcNow);
        var start = from ?? today.AddDays(-1); var end = to ?? today.AddDays(7);
        Rules.Require(end >= start && end.DayNumber - start.DayNumber <= 62, "INVALID_RANGE", "The date range must be ordered and at most 62 days.");
        var me = current.UserId;
        // Draft assignments stay with the Manager until the schedule is published.
        var assignments = await store.List<ShiftAssignment>(a => a.UserId == me && a.WorkDate >= start && a.WorkDate <= end && a.Status != ShiftAssignmentStatus.Draft, ct);
        return (await ShiftViews(assignments, ct)).OrderBy(s => s.StartsAt).ToArray();
    }

    public async Task<ShiftView> CheckIn(Guid shiftAssignmentId, CancellationToken ct)
    {
        UseCase.Staff(current);
        await schema.Require(ct);
        await store.Transaction(async () =>
        {
            var assignment = await store.Find<ShiftAssignment>(shiftAssignmentId, ct);
            if (assignment is null || assignment.UserId != current.UserId || assignment.Status == ShiftAssignmentStatus.Draft)
                throw new ApplicationException("NOT_FOUND", "The resource was not found.", 404);
            var shift = UseCase.Found(await store.Find<Shift>(assignment.ShiftId, ct));
            var (startsAt, endsAt) = calendar.Window(assignment.WorkDate, shift.StartTime, shift.EndTime);
            if (assignment.CheckInAt is null)
            {
                var now = clock.UtcNow; var opensAt = startsAt - TaskRules.CheckInOpensBefore;
                if (assignment.Status == "CANCELLED") throw new ApplicationException("SHIFT_CANCELLED", "This shift assignment was cancelled.");
                if (now >= endsAt) throw new ApplicationException("SHIFT_ENDED", "This shift has already ended.");
                if (now < opensAt) throw new ApplicationException("CHECK_IN_TOO_EARLY", $"Check-in opens at {opensAt:O}.", 422);
                assignment.CheckInAt = now;
                await store.Update(assignment, ct);
            }
            return 0;
        }, ct);
        return (await ShiftViews([UseCase.Found(await store.Find<ShiftAssignment>(shiftAssignmentId, ct))], ct)).Single();
    }

    public async Task<EligibilityView> Eligibility(CancellationToken ct)
    {
        UseCase.Staff(current);
        await schema.Require(ct);
        var me = (await roster.OnShift(clock.UtcNow, ct)).SingleOrDefault(s => s.User.UserId == current.UserId);
        if (me is null)
        {
            var load = (await roster.Workload([current.UserId], ct)).GetValueOrDefault(current.UserId);
            return new(false, null, null, load, TaskRules.MaxActiveTasks, []);
        }
        return new(true, me.Assignment.ShiftAssignmentId, me.EndsAt, me.ActiveTasks, TaskRules.MaxActiveTasks, await ZoneViews(me.ZoneIds, ct));
    }

    public async Task<TaskOfferView[]> Offers(bool openOnly, CancellationToken ct)
    {
        UseCase.Staff(current);
        await schema.Require(ct);
        var me = current.UserId; var since = clock.UtcNow.AddDays(-1);
        var offers = await store.List<TaskAssignmentHistory>(a => a.StaffUserId == me && a.AssignmentType == TaskAssignmentType.Offer
            && (a.Status == OfferStatus.Offered || (!openOnly && a.AssignedAt >= since)), ct);
        return (await views.Offers(offers, ct)).OrderByDescending(o => o.CreatedAt).ToArray();
    }

    public async Task<TaskOfferView> Offer(Guid offerId, CancellationToken ct)
    {
        UseCase.Staff(current);
        await schema.Require(ct);
        return (await views.Offers([await MyOffer(offerId, ct)], ct)).Single();
    }

    /// <summary>Handle Now / Accept and Handle Later / Cannot Handle. The first valid acceptance wins atomically (BR-21).</summary>
    public async Task<TaskResponseResult> Respond(Guid offerId, TaskResponseRequest request, CancellationToken ct)
    {
        UseCase.Staff(current);
        await schema.Require(ct);
        TaskRules.Response(request.Response, request.Reason);
        Guid taskId = Guid.Empty;
        try
        {
            var accepted = await store.Transaction(async () =>
            {
                var offer = await MyOffer(offerId, ct);
                var task = UseCase.Found(await store.Find<WorkTask>(offer.TaskId, ct));
                taskId = task.TaskId;
                if (offer.Status != OfferStatus.Offered || task.Status != WorkTaskStatus.Offered || task.CurrentAssigneeUserId is not null)
                {
                    // A retry of my own acceptance (e.g. after a timeout) returns the task as it is now.
                    if (offer.Status == OfferStatus.Accepted && task.CurrentAssigneeUserId == current.UserId && request.Response != TaskResponse.CannotHandle) return true;
                    if (task.CurrentAssigneeUserId is { } other && other != current.UserId) throw await Taken(task, ct);
                    throw new ApplicationException("OFFER_CLOSED", "This offer is no longer open.");
                }
                var now = clock.UtcNow;
                if (request.Response == TaskResponse.CannotHandle)
                {
                    offer.Status = OfferStatus.Declined; offer.RespondedAt = now; offer.EndedAt = now; offer.Reason = request.Reason!.Trim();
                    await store.Update(offer, ct);
                    var stillOpen = (await store.List<TaskAssignmentHistory>(a => a.TaskId == task.TaskId && a.Status == OfferStatus.Offered, ct))
                        .Any(a => a.TaskAssignmentId != offer.TaskAssignmentId);
                    if (!stillOpen) await dispatch.NeedsOperator(task, "Every offered Staff member answered Cannot handle.", ct);
                    return false;
                }
                var me = (await roster.OnShift(now, ct)).SingleOrDefault(s => s.User.UserId == current.UserId)
                    ?? throw new ApplicationException("NOT_ON_SHIFT", "Check in to a current shift before accepting tasks.");
                if (me.ActiveTasks >= TaskRules.MaxActiveTasks)
                    throw new ApplicationException("WORKLOAD_LIMIT", $"You already have {me.ActiveTasks} active tasks; the limit is {TaskRules.MaxActiveTasks}.");
                if (request.Response == TaskResponse.HandleLater) TaskRules.PlannedStart(request.PlannedStartAt, now, me.EndsAt);
                TaskRules.Accept(task, now, request.Response, request.PlannedStartAt, current.UserId);
                await store.Update(task, ct);
                offer.Status = OfferStatus.Accepted; offer.RespondedAt = now;
                await store.Update(offer, ct);
                await dispatch.CloseOpenOffers(task.TaskId, OfferStatus.Withdrawn, "TAKEN_BY_OTHER", now, ct, offer.TaskAssignmentId);
                return true;
            }, ct);
            return new TaskResponseResult(request.Response, accepted ? await View(taskId, ct) : null);
        }
        catch (ApplicationException e) when (e.Code is "CONCURRENT_UPDATE" or "DATABASE_CONFLICT" && taskId != Guid.Empty)
        {
            // A concurrent acceptance committed first.
            var task = await store.Find<WorkTask>(taskId, ct);
            if (task?.CurrentAssigneeUserId is { } other && other != current.UserId) throw await Taken(task, ct);
            throw;
        }
    }

    public async Task<StaffTaskView[]> Tasks(string? status, CancellationToken ct)
    {
        UseCase.Staff(current);
        await schema.Require(ct);
        var me = current.UserId;
        var statuses = status is null ? [WorkTaskStatus.Accepted, WorkTaskStatus.InProgress, WorkTaskStatus.Submitted] : status.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var s in statuses) Rules.Status(s, WorkTaskStatus.Accepted, WorkTaskStatus.InProgress, WorkTaskStatus.Submitted, WorkTaskStatus.Verified, WorkTaskStatus.Cancelled);
        var tasks = await store.List<WorkTask>(t => t.CurrentAssigneeUserId == me && statuses.Contains(t.Status), ct);
        return (await views.Tasks(tasks, ct)).OrderBy(t => t.PlannedStartAt ?? t.AcceptedAt ?? t.CreatedAt).ToArray();
    }

    public async Task<StaffTaskView> Start(Guid taskId, CancellationToken ct)
    {
        UseCase.Staff(current);
        await schema.Require(ct);
        await store.Transaction(async () =>
        {
            var task = await store.Find<WorkTask>(taskId, ct);
            if (task is null || task.CurrentAssigneeUserId != current.UserId) throw new ApplicationException("NOT_FOUND", "The resource was not found.", 404);
            if (task.Status != WorkTaskStatus.InProgress)
            {
                if (task.Status != WorkTaskStatus.Accepted) throw new ApplicationException("TASK_NOT_ACCEPTED", "Only an accepted task that has not started can be started.");
                task.Status = WorkTaskStatus.InProgress; task.StartedAt = clock.UtcNow;
                await store.Update(task, ct);
            }
            return task.TaskId;
        }, ct);
        return await View(taskId, ct);
    }

    // Views are built after commit so they include rows written by the transaction.
    private async Task<StaffTaskView> View(Guid taskId, CancellationToken ct) => (await views.Tasks([UseCase.Found(await store.Find<WorkTask>(taskId, ct))], ct)).Single();
    private async Task<TaskAssignmentHistory> MyOffer(Guid offerId, CancellationToken ct)
    {
        var offer = await store.Find<TaskAssignmentHistory>(offerId, ct);
        if (offer is null || offer.StaffUserId != current.UserId || offer.AssignmentType != TaskAssignmentType.Offer)
            throw new ApplicationException("NOT_FOUND", "The resource was not found.", 404);
        return offer;
    }
    private async Task<ApplicationException> Taken(WorkTask task, CancellationToken ct)
    {
        var name = task.CurrentAssigneeUserId is { } id ? (await store.Find<UserAccount>(id, ct))?.FullName : null;
        return new ApplicationException("TASK_ALREADY_TAKEN", $"{name ?? "Another Staff member"} accepted this task at {task.AcceptedAt:O}.");
    }
    private async Task<ShiftView[]> ShiftViews(List<ShiftAssignment> assignments, CancellationToken ct)
    {
        var shiftIds = assignments.Select(a => a.ShiftId).Distinct().ToList();
        var shifts = (await store.List<Shift>(s => shiftIds.Contains(s.ShiftId), ct)).ToDictionary(s => s.ShiftId);
        var ids = assignments.Select(a => a.ShiftAssignmentId).ToList();
        var zoneRows = await store.List<StaffZoneAssignment>(z => ids.Contains(z.ShiftAssignmentId) && z.UnassignedAt == null && z.Status == "ACTIVE", ct);
        var zones = (await ZoneViews(zoneRows.Select(z => z.ZoneId), ct)).ToDictionary(z => z.ZoneId);
        return assignments.Where(a => shifts.ContainsKey(a.ShiftId)).Select(a =>
        {
            var shift = shifts[a.ShiftId]; var (startsAt, endsAt) = calendar.Window(a.WorkDate, shift.StartTime, shift.EndTime);
            return new ShiftView(a.ShiftAssignmentId, shift.ShiftId, shift.Name, a.WorkDate, startsAt, endsAt, startsAt - TaskRules.CheckInOpensBefore, a.CheckInAt, a.CheckOutAt, a.Status,
                zoneRows.Where(z => z.ShiftAssignmentId == a.ShiftAssignmentId && zones.ContainsKey(z.ZoneId)).Select(z => zones[z.ZoneId]).DistinctBy(z => z.ZoneId).ToArray());
        }).ToArray();
    }
    private async Task<ShiftZoneView[]> ZoneViews(IEnumerable<Guid> zoneIds, CancellationToken ct)
    {
        var ids = zoneIds.Distinct().ToList();
        return (await store.List<Zone>(z => ids.Contains(z.ZoneId), ct)).Select(z => new ShiftZoneView(z.ZoneId, z.Name)).ToArray();
    }
}
