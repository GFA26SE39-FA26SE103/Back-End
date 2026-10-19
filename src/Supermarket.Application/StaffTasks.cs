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
        var assignments = await store.List<ShiftAssignment>(a => a.UserId == me && a.WorkDate >= start && a.WorkDate <= end, ct);
        return (await ShiftViews(assignments, ct)).OrderBy(s => s.StartsAt).ToArray();
    }

    public async Task<ShiftView> CheckIn(Guid shiftAssignmentId, CancellationToken ct)
    {
        UseCase.Staff(current);
        await schema.Require(ct);
        await store.Transaction(async () =>
        {
            var assignment = await store.Find<ShiftAssignment>(shiftAssignmentId, ct);
            if (assignment is null || assignment.UserId != current.UserId) throw new ApplicationException("NOT_FOUND", "The resource was not found.", 404);
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

    public async Task<NotificationView[]> Notifications(CancellationToken ct)
    {
        UseCase.Staff(current);
        await schema.Require(ct);
        var me = current.UserId;
        var recipients = await store.List<NotificationRecipient>(r => r.UserId == me, ct);
        var ids = recipients.Select(r => r.NotificationId).ToList();
        var notifications = (await store.List<Notification>(n => ids.Contains(n.NotificationId), ct)).ToDictionary(n => n.NotificationId);
        return recipients.Where(r => notifications.ContainsKey(r.NotificationId)).Select(r => (Row: r, N: notifications[r.NotificationId]))
            .OrderByDescending(x => x.N.CreatedAt).Take(100)
            .Select(x => new NotificationView(x.N.NotificationId, x.N.NotificationType, x.N.Title, x.N.Message, x.N.Severity, x.N.TaskId, x.N.EventId, x.N.CreatedAt, x.Row.ReadAt)).ToArray();
    }

    public async Task<int> ReadAll(CancellationToken ct)
    {
        UseCase.Staff(current);
        await schema.Require(ct);
        return await store.Transaction(async () =>
        {
            var me = current.UserId; var now = clock.UtcNow;
            var unread = await store.List<NotificationRecipient>(r => r.UserId == me && r.ReadAt == null, ct);
            foreach (var row in unread)
            {
                row.ReadAt = row.DeliveredAt is { } delivered && delivered > now ? delivered : now;
                await store.Update(row, ct);
            }
            return unread.Count;
        }, ct);
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

/// <summary>Operator task board and manual assignment after escalation (BR-20, BR-22). Manual assignment enters at Accepted.</summary>
public sealed class OperatorTasks(ISetupStore store, ICurrentUser current, IClock clock, StaffRoster roster, TaskDispatch dispatch, TaskNotifier notifier, TaskViews views, ITaskSchema schema)
{
    public async Task<StaffTaskView[]> List(string? status, bool needsAssignment, CancellationToken ct)
    {
        UseCase.Viewer(current);
        await schema.Require(ct);
        var statuses = status is null ? [WorkTaskStatus.Offered, WorkTaskStatus.Accepted, WorkTaskStatus.InProgress, WorkTaskStatus.Submitted]
            : status.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var s in statuses) Rules.Status(s, WorkTaskStatus.Offered, WorkTaskStatus.Accepted, WorkTaskStatus.InProgress, WorkTaskStatus.Submitted, WorkTaskStatus.Verified, WorkTaskStatus.Cancelled);
        var tasks = await store.List<WorkTask>(t => statuses.Contains(t.Status), ct);
        var result = await views.Tasks(tasks.OrderByDescending(t => t.CreatedAt).Take(200).ToList(), ct);
        return result.Where(t => !needsAssignment || t.NeedsAssignment).ToArray();
    }

    public async Task<StaffTaskView> Get(Guid taskId, CancellationToken ct)
    {
        await schema.Require(ct);
        var task = UseCase.Found(await store.Find<WorkTask>(taskId, ct));
        if (current.Role == "STAFF")
        {
            var me = current.UserId;
            var involved = task.CurrentAssigneeUserId == me || (await store.List<TaskAssignmentHistory>(a => a.TaskId == taskId && a.StaffUserId == me, ct)).Count > 0;
            if (!involved) throw new ApplicationException("NOT_FOUND", "The resource was not found.", 404);
        }
        else UseCase.Viewer(current);
        return (await views.Tasks([task], ct)).Single();
    }

    public async Task<AssignableStaffView[]> AssignableStaff(Guid taskId, CancellationToken ct)
    {
        UseCase.Operator(current);
        await schema.Require(ct);
        var task = UseCase.Found(await store.Find<WorkTask>(taskId, ct));
        return (await roster.OnShift(clock.UtcNow, ct)).Where(s => s.ActiveTasks < TaskRules.MaxActiveTasks)
            .Select(s => new AssignableStaffView(s.User.UserId, s.User.FullName, s.ZoneIds.Contains(task.ZoneId), s.ActiveTasks, s.EndsAt))
            .OrderByDescending(s => s.AssignedToZone).ThenBy(s => s.ActiveTasks).ThenBy(s => s.FullName).ToArray();
    }

    public async Task<StaffTaskView> Assign(Guid taskId, TaskAssignRequest request, CancellationToken ct)
    {
        UseCase.Operator(current);
        await schema.Require(ct);
        Rules.Optional(request.Reason, 2000, "Reason");
        await store.Transaction(async () =>
        {
            var task = UseCase.Found(await store.Find<WorkTask>(taskId, ct));
            if (task.Status is not (WorkTaskStatus.Offered or WorkTaskStatus.Accepted))
                throw new ApplicationException("TASK_NOT_ASSIGNABLE", "Only an offered task, or an accepted task that has not started, can be assigned.");
            var now = clock.UtcNow;
            var staff = (await roster.OnShift(now, ct)).SingleOrDefault(s => s.User.UserId == request.StaffUserId)
                ?? throw new ApplicationException("STAFF_NOT_ON_SHIFT", "The Staff member is not checked in to a current shift.");
            if (task.CurrentAssigneeUserId == staff.User.UserId) return 0;
            if (staff.ActiveTasks >= TaskRules.MaxActiveTasks) throw new ApplicationException("WORKLOAD_LIMIT", "The Staff member has reached the active-task limit.");
            foreach (var previous in await store.List<TaskAssignmentHistory>(a => a.TaskId == taskId && a.Status == OfferStatus.Accepted && a.EndedAt == null, ct))
            {
                previous.EndedAt = now; previous.Reason = "REASSIGNED";
                await store.Update(previous, ct);
            }
            await dispatch.CloseOpenOffers(taskId, OfferStatus.Withdrawn, "MANUALLY_ASSIGNED", now, ct);
            await store.Add(new TaskAssignmentHistory { TaskAssignmentId = Guid.NewGuid(), TaskId = taskId, StaffUserId = staff.User.UserId, AssignedByUserId = current.UserId,
                AssignmentType = TaskAssignmentType.Manual, Status = OfferStatus.Accepted, AssignedAt = now, RespondedAt = now, Reason = request.Reason?.Trim() }, ct);
            task.CurrentAssigneeUserId = staff.User.UserId; task.AssignedByUserId = current.UserId; task.AssignedAt = now; task.AcceptedAt = now;
            task.Status = WorkTaskStatus.Accepted; task.PlannedStartAt = null; task.StartedAt = null;
            await store.Update(task, ct);
            await notifier.Send(TaskNotifier.Assigned, $"{task.Priority} · Task assigned to you", task.Title, task, [staff.User.UserId], "MANUAL_ASSIGNEE", ct);
            return 0;
        }, ct);
        return (await views.Tasks([UseCase.Found(await store.Find<WorkTask>(taskId, ct))], ct)).Single();
    }
}

/// <summary>Builds offer/task views with Zone, camera and OperationalEvent context.</summary>
public sealed class TaskViews(ISetupStore store, TaskDispatch dispatch)
{
    public async Task<StaffTaskView[]> Tasks(List<WorkTask> tasks, CancellationToken ct)
    {
        if (tasks.Count == 0) return [];
        var context = await Load(tasks, ct);
        var taskIds = tasks.Select(t => t.TaskId).ToList();
        var assignments = await store.List<TaskAssignmentHistory>(a => taskIds.Contains(a.TaskId), ct);
        var assigneeIds = tasks.Where(t => t.CurrentAssigneeUserId != null).Select(t => t.CurrentAssigneeUserId!.Value).Distinct().ToList();
        var names = (await store.List<UserAccount>(u => assigneeIds.Contains(u.UserId), ct)).ToDictionary(u => u.UserId, u => u.FullName);
        return tasks.Select(t =>
        {
            var ev = t.EventId is { } eventId ? context.Events.GetValueOrDefault(eventId) : null;
            var current = assignments.Where(a => a.TaskId == t.TaskId && a.StaffUserId == t.CurrentAssigneeUserId && a.Status == OfferStatus.Accepted && a.EndedAt == null)
                .OrderByDescending(a => a.AssignedAt).FirstOrDefault();
            var needsAssignment = t.Status == WorkTaskStatus.Offered && t.CurrentAssigneeUserId is null && !assignments.Any(a => a.TaskId == t.TaskId && a.Status == OfferStatus.Offered);
            var firstPriority = context.FirstOfferPriority.GetValueOrDefault(t.TaskId);
            return new StaffTaskView(t.TaskId, t.OriginType, SourceCode(t), ev is null ? null : context.EventTypes.GetValueOrDefault(ev.EventTypeId)?.Code, t.Title, t.Description, t.Priority,
                firstPriority is not null && TaskRules.Rank(firstPriority) < TaskRules.Rank(t.Priority) ? firstPriority : null,
                t.ZoneId, context.Zones.GetValueOrDefault(t.ZoneId)?.Name ?? "", ev is null ? null : context.Cameras.GetValueOrDefault(ev.CameraId)?.Code, t.Status,
                current?.AssignmentType ?? TaskAssignmentType.Offer, t.CurrentAssigneeUserId, t.CurrentAssigneeUserId is { } id ? names.GetValueOrDefault(id) : null,
                t.CreatedAt, t.AssignedAt, t.AcceptedAt, t.PlannedStartAt, t.StartedAt, t.SubmittedAt, ev?.ClearedAt, needsAssignment, t.UpdatedAt);
        }).ToArray();
    }

    public async Task<TaskOfferView[]> Offers(List<TaskAssignmentHistory> offers, CancellationToken ct)
    {
        if (offers.Count == 0) return [];
        var taskIds = offers.Select(o => o.TaskId).Distinct().ToList();
        var tasks = (await store.List<WorkTask>(t => taskIds.Contains(t.TaskId), ct)).ToDictionary(t => t.TaskId);
        var context = await Load(tasks.Values.ToList(), ct);
        var allOffers = await store.List<TaskAssignmentHistory>(a => taskIds.Contains(a.TaskId) && a.AssignmentType == TaskAssignmentType.Offer, ct);
        var rounds = await store.List<Notification>(n => n.TaskId != null && taskIds.Contains(n.TaskId.Value) && n.NotificationType == TaskNotifier.Offer, ct);
        var (interval, maxAttempts) = await dispatch.Retry(ct);
        return offers.Where(o => tasks.ContainsKey(o.TaskId)).Select(o =>
        {
            var t = tasks[o.TaskId];
            var ev = t.EventId is { } eventId ? context.Events.GetValueOrDefault(eventId) : null;
            var taskRounds = rounds.Where(n => n.TaskId == t.TaskId).ToList();
            var attempt = Math.Max(1, taskRounds.Count);
            DateTime? next = o.Status == OfferStatus.Offered && taskRounds.Count is > 0 && taskRounds.Count < maxAttempts ? taskRounds.Max(n => n.CreatedAt).AddSeconds(interval) : null;
            var camera = ev is null ? null : context.Cameras.GetValueOrDefault(ev.CameraId);
            var (mode, unit) = Measurement(ev?.MetadataJson);
            return new TaskOfferView(o.TaskAssignmentId, t.TaskId, t.OriginType, SourceCode(t), ev is null ? null : context.EventTypes.GetValueOrDefault(ev.EventTypeId)?.Code,
                t.Title, t.Description, t.Priority, t.ZoneId, context.Zones.GetValueOrDefault(t.ZoneId)?.Name ?? "", camera?.Code, camera?.Name, ev?.MetricValue, mode, unit,
                o.AssignedAt, allOffers.Where(a => a.TaskId == t.TaskId).Select(a => a.StaffUserId).Distinct().Count(), attempt, maxAttempts, next, o.Status, o.Reason, ev?.ClearedAt);
        }).ToArray();
    }

    private sealed record Context(Dictionary<Guid, Zone> Zones, Dictionary<Guid, OperationalEvent> Events, Dictionary<Guid, Camera> Cameras,
        Dictionary<Guid, OperationalEventType> EventTypes, Dictionary<Guid, string?> FirstOfferPriority);
    private async Task<Context> Load(List<WorkTask> tasks, CancellationToken ct)
    {
        var zoneIds = tasks.Select(t => t.ZoneId).Distinct().ToList();
        var eventIds = tasks.Where(t => t.EventId != null).Select(t => t.EventId!.Value).Distinct().ToList();
        var taskIds = tasks.Select(t => t.TaskId).ToList();
        var zones = (await store.List<Zone>(z => zoneIds.Contains(z.ZoneId), ct)).ToDictionary(z => z.ZoneId);
        var events = eventIds.Count == 0 ? [] : (await store.List<OperationalEvent>(e => eventIds.Contains(e.EventId), ct)).ToDictionary(e => e.EventId);
        var cameraIds = events.Values.Select(e => e.CameraId).Distinct().ToList();
        var cameras = cameraIds.Count == 0 ? [] : (await store.List<Camera>(c => cameraIds.Contains(c.CameraId), ct)).ToDictionary(c => c.CameraId);
        var typeIds = events.Values.Select(e => e.EventTypeId).Distinct().ToList();
        var types = typeIds.Count == 0 ? [] : (await store.List<OperationalEventType>(t => typeIds.Contains(t.EventTypeId), ct)).ToDictionary(t => t.EventTypeId);
        var firstOffers = (await store.List<Notification>(n => n.TaskId != null && taskIds.Contains(n.TaskId.Value) && n.NotificationType == TaskNotifier.Offer, ct))
            .GroupBy(n => n.TaskId!.Value).ToDictionary(g => g.Key, g => g.OrderBy(n => n.CreatedAt).First().Severity);
        return new(zones, events, cameras, types, firstOffers);
    }
    private static string SourceCode(WorkTask task)
    {
        var (prefix, id) = task.OriginType switch
        {
            TaskOrigin.OperationalEvent => ("EVT", task.EventId),
            TaskOrigin.Incident => ("INC", task.IncidentId),
            TaskOrigin.StaffReport => ("RPT", task.StaffReportId),
            _ => ("TSK", (Guid?)task.TaskId)
        };
        return $"{prefix}-{(id ?? task.TaskId).ToString("N")[..8].ToUpperInvariant()}";
    }
    private static (string? Mode, string? Unit) Measurement(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return (null, null);
        try
        {
            using var json = JsonDocument.Parse(metadata);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null);
            return (Text(root, "measurementMode"), Text(root, "thresholdUnit"));
        }
        catch (JsonException) { return (null, null); }
    }
    private static string? Text(JsonElement root, string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
