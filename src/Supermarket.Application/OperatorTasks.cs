using Supermarket.Domain;
namespace Supermarket.Application;

public interface IOperatorTaskQueries
{
    Task<Guid[]> NeedingAssignmentIds(int limit, CancellationToken ct);
}

public sealed record ManualTaskRequest(Guid TaskId, Guid ZoneId, string TaskTypeCode, string Title, string? Instructions);

/// <summary>Operator task board and manual assignment after escalation (BR-20, BR-22). Staff must accept a manual offer.</summary>
public sealed class OperatorTasks(ISetupStore store, ICurrentUser current, IClock clock, StaffRoster roster, TaskDispatch dispatch, TaskNotifier notifier, TaskViews views,
    ITaskSchema schema, IOperatorTaskQueries queries, StandaloneTaskSlaPolicyService taskSla)
{
    private const int ResultLimit = 200;

    public async Task<StaffTaskView> CreateManual(ManualTaskRequest request, CancellationToken ct)
    {
        UseCase.Operator(current);
        await schema.Require(ct);
        if (request.TaskId == Guid.Empty || request.ZoneId == Guid.Empty)
            throw new ApplicationException("TASK_SOURCE_REQUIRED", "Provide Task and Zone IDs.", 422);
        var title = Rules.Text(request.Title, 200, "Title").Trim();
        Rules.Optional(request.Instructions, 4000, "Instructions");
        var code = Rules.Text(request.TaskTypeCode, 50, "Task type code").Trim().ToUpperInvariant();
        if (!Enumerable.Range(1, 11).Select(number => $"TT-{number:00}").Contains(code))
            throw new ApplicationException("MANUAL_TASK_TYPE_INVALID", "Select an approved standalone Task type.", 422);
        var types = await store.List<TaskType>(item => item.Code == code, ct);
        if (types.Count != 1) throw new ApplicationException("MANUAL_TASK_TYPE_INVALID", "Task type is missing or ambiguous.", 422);
        var type = types[0];
        if (type.RequiredSpecialtyId == Guid.Empty)
            throw new ApplicationException("MANUAL_TASK_TYPE_INVALID", "Task type has no required specialty.", 422);
        var zone = UseCase.Found(await store.Find<Zone>(request.ZoneId, ct));
        if (zone.Status != "ACTIVE") throw new ApplicationException("ZONE_INACTIVE", "The selected Zone is inactive.", 422);
        var task = await store.Transaction(async () =>
        {
            var existing = await store.Find<WorkTask>(request.TaskId, ct);
            if (existing is not null)
            {
                if (existing.OriginType != TaskOrigin.Manual || existing.ZoneId != request.ZoneId || existing.TaskTypeId != type.TaskTypeId
                    || existing.Title != title || existing.Description != request.Instructions?.Trim())
                    throw new ApplicationException("TASK_ID_CONFLICT", "This Task ID was used for different content.", 409);
                return existing;
            }
            var created = new WorkTask { TaskId = request.TaskId, ZoneId = request.ZoneId, OriginType = TaskOrigin.Manual,
                TaskTypeId = type.TaskTypeId, Title = title, Description = request.Instructions?.Trim(), Status = WorkTaskStatus.Offered };
            await taskSla.Apply(created, ct);
            await store.Add(created, ct);
            await dispatch.OfferStandaloneTask(created, ct);
            return created;
        }, ct);
        return (await views.Tasks([task], ct)).Single();
    }

    public async Task<TaskOfferView[]> Offers(string? status, Guid? taskId, CancellationToken ct)
    {
        UseCase.Operator(current);
        await schema.Require(ct);
        var statuses = status is null ? new[] { TaskOfferStatus.Pending, TaskOfferStatus.Accepted, TaskOfferStatus.Declined, TaskOfferStatus.Expired, TaskOfferStatus.Cancelled }
            : status.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var value in statuses) Rules.Status(value, TaskOfferStatus.Pending, TaskOfferStatus.Accepted, TaskOfferStatus.Declined, TaskOfferStatus.Expired, TaskOfferStatus.Cancelled);
        var rows = await store.List<TaskOffer>(o => statuses.Contains(o.Status) && (taskId == null || o.TaskId == taskId), ct);
        return await views.Offers(rows.OrderByDescending(o => o.OfferedAt).Take(ResultLimit).ToList(), ct);
    }

    public async Task<TaskOfferView> Offer(Guid offerId, CancellationToken ct)
    {
        UseCase.Operator(current);
        await schema.Require(ct);
        var offer = UseCase.Found(await store.Find<TaskOffer>(offerId, ct));
        return (await views.Offers([offer], ct)).Single();
    }

    public async Task<StaffTaskView[]> List(string? status, bool needsAssignment, CancellationToken ct)
    {
        UseCase.Viewer(current);
        await schema.Require(ct);
        var statuses = status is null ? [WorkTaskStatus.Offered, WorkTaskStatus.Accepted, WorkTaskStatus.InProgress, WorkTaskStatus.Submitted]
            : status.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var s in statuses) Rules.Status(s, WorkTaskStatus.Offered, WorkTaskStatus.Accepted, WorkTaskStatus.InProgress, WorkTaskStatus.Submitted, WorkTaskStatus.Verified, WorkTaskStatus.Cancelled);
        if (!needsAssignment)
        {
            var tasks = await store.List<WorkTask>(t => statuses.Contains(t.Status), ct);
            return await views.Tasks(tasks.OrderByDescending(t => t.CreatedAt).Take(ResultLimit).ToList(), ct);
        }

        if (!statuses.Contains(WorkTaskStatus.Offered)) return [];
        var ids = await queries.NeedingAssignmentIds(ResultLimit, ct);
        if (ids.Length == 0) return [];
        var byId = (await store.List<WorkTask>(t => ids.Contains(t.TaskId), ct)).ToDictionary(t => t.TaskId);
        return await views.Tasks(ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList(), ct);
    }

    public async Task<StaffTaskView> Get(Guid taskId, CancellationToken ct)
    {
        await schema.Require(ct);
        var task = UseCase.Found(await store.Find<WorkTask>(taskId, ct));
        if (current.Role == "STAFF")
        {
            var me = current.UserId;
            var involved = task.CurrentAssigneeUserId == me
                || (await store.List<TaskOffer>(o => o.TaskId == taskId && o.StaffUserId == me, ct)).Count > 0
                || (await store.List<TaskAssignmentHistory>(a => a.TaskId == taskId && a.StaffUserId == me, ct)).Count > 0;
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
        var excluded = await RejectedPerformers(taskId, ct);
        var onShift = await roster.OnShift(clock.UtcNow, ct);
        var specialty = task.TaskTypeId is { } taskTypeId ? (await store.Find<TaskType>(taskTypeId, ct))?.RequiredSpecialtyId : null;
        // Historical/manual tasks may predate V7 TaskType data. Enforce specialty whenever
        // the task is typed; keep those legacy tasks manually assignable for migration safety.
        return onShift.Where(s => (specialty is null || s.User.SpecialtyId == specialty)
                && s.ActiveTasks < TaskRules.MaxActiveTasks && !excluded.Contains(s.User.UserId))
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
            if (request.ExpectedUpdatedAt is { } expected && expected != task.UpdatedAt)
                throw new ApplicationException("TASK_CHANGED", "This task changed. Reload its current assignment before assigning again.");
            if (task.Status is not (WorkTaskStatus.Offered or WorkTaskStatus.Accepted))
                throw new ApplicationException("TASK_NOT_ASSIGNABLE", "Only an offered task, or an accepted task that has not started, can be assigned.");
            var now = clock.UtcNow;
            var staff = (await roster.OnShift(now, ct)).SingleOrDefault(s => s.User.UserId == request.StaffUserId)
                ?? throw new ApplicationException("STAFF_NOT_ON_SHIFT", "The Staff member is not checked in to a current shift.");
            if (task.CurrentAssigneeUserId == staff.User.UserId) return 0;
            if ((await RejectedPerformers(taskId, ct)).Contains(staff.User.UserId))
                throw new ApplicationException("STAFF_REJECTED_TWICE", "This Staff member's rejected work requires assigning a different Staff member.");
            if (staff.ActiveTasks >= TaskRules.MaxActiveTasks) throw new ApplicationException("WORKLOAD_LIMIT", "The Staff member has reached the active-task limit.");
            if (task.TaskTypeId is not null && !await roster.HasRequiredSpecialty(task, staff.User, ct))
                throw new ApplicationException("SPECIALTY_MISMATCH", "The Staff member's specialty does not match this TaskType.", 422);
            if (task.CurrentAssigneeUserId is not null || !staff.ZoneIds.Contains(task.ZoneId))
                Rules.Require(!string.IsNullOrWhiteSpace(request.Reason), "REASON_REQUIRED", "A reason is required for reassignment or assignment outside the Task Zone.");
            await dispatch.CloseOpenOffers(taskId, TaskOfferStatus.Cancelled, "MANUALLY_ASSIGNED", now, ct);
            var offer = new TaskOffer
            {
                OfferId = Guid.NewGuid(), TaskId = taskId, StaffUserId = staff.User.UserId, DispatchCycleId = Guid.NewGuid(),
                WaveNumber = 1, DispatchScope = TaskDispatchScope.Manual, Status = TaskOfferStatus.Pending,
                OfferedAt = now, ExpiresAt = now.AddSeconds(await dispatch.OfferTimeout(task, ct))
            };
            await store.Add(offer, ct);
            await store.Add(new TaskAssignmentHistory { TaskAssignmentId = Guid.NewGuid(), TaskId = taskId, StaffUserId = staff.User.UserId, AssignedByUserId = current.UserId,
                AssignmentType = TaskAssignmentType.Manual, Status = OfferStatus.Pending, AssignedAt = now, Reason = request.Reason?.Trim() }, ct);
            // The proposal is part of the Task's assignment state; bump its concurrency version so
            // another Operator cannot replace it using the stale board snapshot.
            await store.Update(task, ct);
            await notifier.Send(TaskNotifier.Offer, $"{task.Priority} · Task assignment request", task.Title, task, [staff.User.UserId], "MANUAL_ASSIGNEE", ct);
            return 0;
        }, ct);
        return (await views.Tasks([UseCase.Found(await store.Find<WorkTask>(taskId, ct))], ct)).Single();
    }

    /// <summary>Staff excluded after either two initial rejections or a rejected replacement submission.</summary>
    private async Task<HashSet<Guid>> RejectedPerformers(Guid taskId, CancellationToken ct)
        => (await store.List<TaskAssignmentHistory>(a => a.TaskId == taskId
            && TaskEvidenceRules.ReassignmentExclusionReasons.Contains(a.Reason!), ct)).Select(a => a.StaffUserId).ToHashSet();
}
