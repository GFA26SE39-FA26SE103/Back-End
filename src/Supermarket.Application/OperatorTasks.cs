using Supermarket.Domain;
namespace Supermarket.Application;

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
