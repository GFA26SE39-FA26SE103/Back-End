using System.Text.Json;
using Supermarket.Domain;
namespace Supermarket.Application;

/// <summary>Converts a shift's local work date and times to UTC in the single store's time zone.</summary>
public sealed class StoreCalendar(TimeZoneInfo zone)
{
    /// <summary>Configured IANA/Windows id, default Asia/Ho_Chi_Minh; falls back to fixed UTC+7 when the host has no time-zone data.</summary>
    public static TimeZoneInfo Zone(string? id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(id) ? "Asia/Ho_Chi_Minh" : id); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException && string.IsNullOrWhiteSpace(id))
        { return TimeZoneInfo.CreateCustomTimeZone("UTC+07", TimeSpan.FromHours(7), "UTC+07", "UTC+07"); }
    }
    public TimeZoneInfo TimeZone => zone;
    public DateOnly Today(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));
    /// <summary>UTC instant of local midnight at the start of <paramref name="date"/>.</summary>
    public DateTime DayStart(DateOnly date) => Utc(date.ToDateTime(TimeOnly.MinValue));
    /// <summary>An end time at or before the start time means the shift ends the next day.</summary>
    public (DateTime StartsAt, DateTime EndsAt) Window(DateOnly workDate, TimeOnly start, TimeOnly end)
    {
        var startsAt = Utc(workDate.ToDateTime(start));
        var endsAt = Utc((end <= start ? workDate.AddDays(1) : workDate).ToDateTime(end));
        return (startsAt, endsAt);
    }
    private DateTime Utc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
}

public sealed record OnShiftStaff(UserAccount User, ShiftAssignment Assignment, DateTime EndsAt, Guid[] ZoneIds, int ActiveTasks);

/// <summary>Who is on shift, checked in, assigned to which Zones, and how loaded (BR-19, BR-23, BR-35).</summary>
public sealed class StaffRoster(ISetupStore store, StoreCalendar calendar)
{
    public async Task<List<OnShiftStaff>> OnShift(DateTime now, CancellationToken ct)
    {
        var staffRole = (await store.List<Role>(r => r.Name == "STAFF", ct)).SingleOrDefault();
        if (staffRole is null) return [];
        var today = calendar.Today(now); var yesterday = today.AddDays(-1);
        var assignments = await store.List<ShiftAssignment>(a => a.CheckInAt != null && a.CheckOutAt == null && a.WorkDate >= yesterday && a.WorkDate <= today, ct);
        if (assignments.Count == 0) return [];
        var shiftIds = assignments.Select(a => a.ShiftId).Distinct().ToList();
        var shifts = (await store.List<Shift>(s => shiftIds.Contains(s.ShiftId), ct)).ToDictionary(s => s.ShiftId);
        var current = assignments.Where(a => a.Status != ShiftAssignmentStatus.Cancelled && a.Status != ShiftAssignmentStatus.Draft && shifts.TryGetValue(a.ShiftId, out var shift)
            && Contains(calendar.Window(a.WorkDate, shift.StartTime, shift.EndTime), now)).ToList();
        var userIds = current.Select(a => a.UserId).Distinct().ToList();
        var users = (await store.List<UserAccount>(u => userIds.Contains(u.UserId) && u.RoleId == staffRole.RoleId && u.Status == "ACTIVE", ct)).ToDictionary(u => u.UserId);
        var assignmentIds = current.Select(a => a.ShiftAssignmentId).ToList();
        var zones = await store.List<StaffZoneAssignment>(z => assignmentIds.Contains(z.ShiftAssignmentId) && z.UnassignedAt == null && z.Status == "ACTIVE", ct);
        var load = await Workload(users.Keys, ct);
        return current.Where(a => users.ContainsKey(a.UserId))
            .GroupBy(a => a.UserId).Select(g => g.OrderByDescending(a => a.CheckInAt).First())
            .Select(a => new OnShiftStaff(users[a.UserId], a, calendar.Window(a.WorkDate, shifts[a.ShiftId].StartTime, shifts[a.ShiftId].EndTime).EndsAt,
                zones.Where(z => z.ShiftAssignmentId == a.ShiftAssignmentId).Select(z => z.ZoneId).Distinct().ToArray(), load.GetValueOrDefault(a.UserId)))
            .ToList();
    }
    public async Task<List<OnShiftStaff>> Eligible(WorkTask task, DateTime now, CancellationToken ct)
        => await Eligible(task, now, [task.ZoneId], ct);

    public async Task<List<OnShiftStaff>> Eligible(WorkTask task, DateTime now, IReadOnlyCollection<Guid> zoneIds, CancellationToken ct)
    {
        var taskType = task.TaskTypeId is { } taskTypeId ? await store.Find<TaskType>(taskTypeId, ct) : null;
        if (taskType is null) return [];
        return (await OnShift(now, ct)).Where(s => s.ZoneIds.Any(zoneIds.Contains)
            && s.ActiveTasks < TaskRules.MaxActiveTasks
            && s.User.SpecialtyId == taskType.RequiredSpecialtyId).ToList();
    }
    public async Task<bool> HasRequiredSpecialty(WorkTask task, UserAccount staff, CancellationToken ct)
    {
        var taskType = task.TaskTypeId is { } taskTypeId ? await store.Find<TaskType>(taskTypeId, ct) : null;
        return taskType is not null && staff.SpecialtyId == taskType.RequiredSpecialtyId;
    }
    public async Task<Dictionary<Guid, int>> Workload(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.ToList();
        var active = WorkTaskStatus.Active;
        var tasks = await store.List<WorkTask>(t => t.CurrentAssigneeUserId != null && ids.Contains(t.CurrentAssigneeUserId.Value) && active.Contains(t.Status), ct);
        return tasks.GroupBy(t => t.CurrentAssigneeUserId!.Value).ToDictionary(g => g.Key, g => g.Count());
    }
    private static bool Contains((DateTime StartsAt, DateTime EndsAt) window, DateTime now) => now >= window.StartsAt && now < window.EndsAt;
}

/// <summary>Writes Notification + in-app NotificationRecipient rows.</summary>
public sealed class TaskNotifier(ISetupStore store, IClock clock)
{
    public async Task IncidentPolicyException(Incident incident, string code, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var notification = new Notification { NotificationId = Guid.NewGuid(), IncidentId = incident.IncidentId,
            NotificationType = "INCIDENT_POLICY_EXCEPTION", Title = "Incident needs SLA policy review",
            Message = $"Incident {incident.IncidentId}: {code}. Review the Staff Report and configure a valid policy.", CreatedAt = now };
        await store.Add(notification, ct);
        foreach (var userId in await Operators(ct))
            await store.Add(new NotificationRecipient { NotificationRecipientId = Guid.NewGuid(),
                NotificationId = notification.NotificationId, UserId = userId, DeliveryChannel = "IN_APP",
                DeliveryStatus = "SENT", RecipientReason = code, DeliveredAt = now }, ct);
    }
    public const string Offer = "TASK_OFFER", DispatchRing = "TASK_DISPATCH_RING", OfferWithdrawn = "TASK_OFFER_WITHDRAWN", Assigned = "TASK_ASSIGNED", NeedsAssignment = "TASK_NEEDS_ASSIGNMENT",
        PriorityRaised = "TASK_PRIORITY_RAISED", EventCleared = "EVENT_CLEARED";
    public async Task<Notification> Send(string type, string title, string message, WorkTask task, IEnumerable<Guid> recipients, string reason, CancellationToken ct, Guid? cameraId = null)
    {
        var now = clock.UtcNow;
        var notification = new Notification { NotificationId = Guid.NewGuid(), TaskId = task.TaskId, EventId = task.EventId, IncidentId = task.IncidentId, CameraId = cameraId,
            NotificationType = type, Title = Cut(title, 200), Message = Cut(message, 4000), Severity = task.Priority, CreatedAt = now };
        await store.Add(notification, ct);
        foreach (var userId in recipients.Distinct())
            await store.Add(new NotificationRecipient { NotificationRecipientId = Guid.NewGuid(), NotificationId = notification.NotificationId, UserId = userId,
                DeliveryChannel = "IN_APP", DeliveryStatus = "SENT", RecipientReason = reason, DeliveredAt = now }, ct);
        return notification;
    }
    /// <summary>A notification that is not about a Task (for example a shift swap).</summary>
    public async Task<Notification> Broadcast(string type, string title, string message, IEnumerable<Guid> recipients, string reason, CancellationToken ct,
        Guid? eventId = null, string severity = "INFO", Guid? cameraId = null)
    {
        var now = clock.UtcNow;
        var notification = new Notification { NotificationId = Guid.NewGuid(), NotificationType = type, Title = Cut(title, 200), Message = Cut(message, 4000), CreatedAt = now,
            EventId = eventId, Severity = severity, CameraId = cameraId };
        await store.Add(notification, ct);
        foreach (var userId in recipients.Distinct())
            await store.Add(new NotificationRecipient { NotificationRecipientId = Guid.NewGuid(), NotificationId = notification.NotificationId, UserId = userId,
                DeliveryChannel = "IN_APP", DeliveryStatus = "SENT", RecipientReason = reason, DeliveredAt = now }, ct);
        return notification;
    }
    public Task<List<Guid>> Operators(CancellationToken ct) => RoleUsers("OPERATOR", ct);
    public async Task<List<Guid>> RoleUsers(string roleName, CancellationToken ct)
    {
        var role = (await store.List<Role>(r => r.Name == roleName, ct)).SingleOrDefault();
        return role is null ? [] : (await store.List<UserAccount>(u => u.RoleId == role.RoleId && u.Status == "ACTIVE", ct)).Select(u => u.UserId).ToList();
    }
    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>Required ERD V7 task, offer, type and specialty structures.</summary>
public interface ITaskSchema
{
    Task<bool> Ready(CancellationToken ct);
}
public static class TaskSchemaCheck
{
    public static async Task Require(this ITaskSchema schema, CancellationToken ct)
    {
        if (!await schema.Ready(ct))
            throw new ApplicationException("TASK_SCHEMA_NOT_READY", "Apply the approved FA26SE103 ERD V7 database migration before using staff tasks.", 503);
    }
}

/// <summary>Response hook called inside the OperationalEvent episode transaction.</summary>
public interface IOperationalEventResponder
{
    Task Raised(OperationalEvent operationalEvent, string? previousPriority, CancellationToken ct);
    Task Cleared(OperationalEvent operationalEvent, CancellationToken ct);
}

/// <summary>
/// OperationalEvent → SLAPolicy → alert-conditioned action → notification or Task offered to eligible Zone Staff,
/// re-offered every retry interval, then handed to the Operator for manual assignment (BR-19, BR-21, BR-22).
/// One episode yields at most one Task. Alert-level changes never rewrite the Task's snapshotted SLA priority.
/// </summary>
public sealed class TaskDispatch(ISetupStore store, StaffRoster roster, TaskNotifier notifier, TaskVerification verification, IClock clock, ITaskSchema schema) : IOperationalEventResponder
{
    public async Task OfferIncidentTask(WorkTask task, CancellationToken ct)
    {
        if (task.OriginType != TaskOrigin.Incident || task.IncidentId is null || task.TaskTypeId is null || task.SlaPolicyId is null)
            throw new ApplicationException("INCIDENT_TASK_INVALID", "Incident Task requires a source, TaskType and SLA policy.", 422);
        await schema.Require(ct);
        await Advance(task, null, ct);
    }
    public async Task OfferStandaloneTask(WorkTask task, CancellationToken ct)
    {
        if (task.OriginType is not (TaskOrigin.StaffReport or TaskOrigin.Manual) || task.TaskTypeId is null
            || task.SlaPolicyId is null || task.SlaSnapshotJson is null)
            throw new ApplicationException("STANDALONE_TASK_INVALID", "Standalone Task requires a TaskType and SLA snapshot.", 422);
        await schema.Require(ct);
        await Advance(task, null, ct);
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record TaskParameters(string? Title, string? Instructions, string? TaskTypeCode);
    private sealed record OperationalSlaSnapshot(Guid PolicyId, int PolicyVersion, Guid EventTypeId, string PriorityCode,
        int ResponseTargetSec, int ResolutionTargetSec, int OfferTimeoutSec, int WarningPercent, DateTime AppliedAt,
        DateTime ResponseDeadlineAt, DateTime ResolutionDeadlineAt);

    public async Task Raised(OperationalEvent ev, string? previousPriority, CancellationToken ct)
    {
        // Monitoring keeps working on a database without the task schema; there is simply no dispatch.
        if (!await schema.Ready(ct)) return;
        var existing = (await store.List<WorkTask>(t => t.EventId == ev.EventId, ct)).FirstOrDefault();
        if (existing is not null)
            return;
        var match = await TaskAction(ev, ct);
        if (match is null) return;
        var (policy, action) = match.Value;
        if (action.ActionType == OperationalSlaPolicyRules.NotifyOperator)
        {
            // L7: the WARNING action may target Managers instead of the default Operators.
            var targetRole = action.TargetRoleId is Guid roleId ? await store.Find<Role>(roleId, ct) : null;
            await EventNotice(ev, "OPERATIONAL_EVENT_WARNING", "Operational warning", "Review the operational event.", ct, targetRole?.Name);
            return;
        }

        var parameters = Parameters(action.ActionParametersJson);
        if (parameters is null || parameters.Instructions?.Length > 4000)
        {
            await ConfigurationException(ev, "ACTION_PARAMETERS_INVALID", ct);
            return;
        }
        var zone = await store.Find<Zone>(ev.ZoneId, ct);
        var type = await store.Find<OperationalEventType>(ev.EventTypeId, ct);
        var taskType = await ResolveTaskType(parameters?.TaskTypeCode, type, ct);
        // V7 dispatch is specialty-aware. A missing/ambiguous TaskType must be fixed in policy configuration;
        // silently creating a specialty-less Task would fabricate eligibility.
        if (taskType is null)
        {
            await ConfigurationException(ev, "TASK_TYPE_MISSING_OR_AMBIGUOUS", ct);
            return;
        }
        var title = parameters?.Title is { Length: > 0 } custom ? custom : $"{type?.Name ?? "Operational event"} — {zone?.Name ?? "Zone"}";
        var now = clock.UtcNow;
        var snapshot = new OperationalSlaSnapshot(policy.PolicyId, policy.Version, ev.EventTypeId, policy.PriorityCode,
            policy.ResponseTargetSec, policy.ResolutionTargetSec, policy.OfferTimeoutSec, policy.WarningPercent, now,
            now.AddSeconds(policy.ResponseTargetSec), now.AddSeconds(policy.ResolutionTargetSec));
        var task = new WorkTask { TaskId = Guid.NewGuid(), ZoneId = ev.ZoneId, Title = title.Length <= 200 ? title : title[..200], Description = parameters?.Instructions,
            Priority = policy.PriorityCode, Status = WorkTaskStatus.Offered, OriginType = TaskOrigin.OperationalEvent, EventId = ev.EventId, TaskTypeId = taskType.TaskTypeId,
            SlaPolicyId = policy.PolicyId, SlaSnapshotJson = JsonSerializer.Serialize(snapshot, Json),
            ResponseDeadlineAt = snapshot.ResponseDeadlineAt, ResolutionDeadlineAt = snapshot.ResolutionDeadlineAt };
        TaskVerificationRules.Initialize(task, taskType, policy.PriorityCode);
        await store.Add(task, ct);
        await Advance(task, ev.CameraId, ct);
    }

    /// <summary>
    /// Provisional (open point 5): open offers are withdrawn and the unaccepted Task is cancelled;
    /// an accepted Task stays with its Staff member, who is told the area is back to normal.
    /// </summary>
    public async Task Cleared(OperationalEvent ev, CancellationToken ct)
    {
        if (!await schema.Ready(ct)) return;
        var now = clock.UtcNow;
        foreach (var task in await store.List<WorkTask>(t => t.EventId == ev.EventId && t.Status != WorkTaskStatus.Cancelled && t.Status != WorkTaskStatus.Verified, ct))
        {
            if (task.Status == WorkTaskStatus.Offered && task.CurrentAssigneeUserId is null)
            {
                var withdrawn = await CloseOpenOffers(task.TaskId, TaskOfferStatus.Cancelled, "EVENT_CLEARED", now, ct);
                task.Status = WorkTaskStatus.Cancelled;
                await store.Update(task, ct);
                if (withdrawn.Count > 0)
                    await notifier.Send(TaskNotifier.OfferWithdrawn, "No longer needed", $"{task.Title}: the camera shows the area back to normal.", task, withdrawn, "OFFER_RECIPIENT", ct, ev.CameraId);
            }
            else if (task.CurrentAssigneeUserId is { } assignee)
            {
                await notifier.Send(TaskNotifier.EventCleared, "Event cleared", $"{task.Title}: the camera shows the area back to normal. Check the area and finish the task.", task, [assignee], "ASSIGNEE", ct, ev.CameraId);
                await verification.TryAuto(task, ct);
            }
        }
    }

    /// <summary>Background retry: expire V7 offers, open a new dispatch cycle, then ask the Operator after exhaustion.</summary>
    public async Task<int> Tick(CancellationToken ct)
    {
        if (!await schema.Ready(ct)) return 0;
        return await store.Transaction(() => RetryRound(ct), ct);
    }
    private async Task<int> RetryRound(CancellationToken ct)
    {
        var now = clock.UtcNow; var changedTasks = new HashSet<Guid>();
        foreach (var task in await store.List<WorkTask>(t => t.Status == WorkTaskStatus.Offered || t.Status == WorkTaskStatus.Accepted, ct))
        {
            var waitsForOperator = (await store.List<TaskAssignmentHistory>(a => a.TaskId == task.TaskId
                && TaskEvidenceRules.ReassignmentExclusionReasons.Contains(a.Reason!), ct)).Count > 0;
            var offers = await store.List<TaskOffer>(o => o.TaskId == task.TaskId, ct);
            foreach (var expired in offers.Where(o => o.Status == TaskOfferStatus.Pending && o.ExpiresAt <= now))
            {
                expired.Status = TaskOfferStatus.Expired;
                expired.RespondedAt = now;
                expired.DeclineReasonCode = "NO_RESPONSE";
                await store.Update(expired, ct);
                await Audit(expired, OfferStatus.Expired, "NO_RESPONSE", now, ct);
                changedTasks.Add(task.TaskId);
            }
            // A manual reassignment proposal may coexist with the current accepted assignee.
            // Expire it, but do not disturb the current assignment or restart automatic dispatch.
            if (task.Status != WorkTaskStatus.Offered || task.CurrentAssigneeUserId is not null) continue;
            if (offers.Any(o => o.Status == TaskOfferStatus.Pending && o.ExpiresAt > now)) continue;
            // Once an Operator has selected a Staff member, a declined/expired proposal returns to
            // the Operator instead of silently restarting the automatic dispatch rings.
            if (offers.Any(o => o.DispatchScope == TaskDispatchScope.Manual))
            {
                if (await NeedsOperator(task, "The manually selected Staff member did not accept the task.", ct)) changedTasks.Add(task.TaskId);
                continue;
            }
            // After automatic reassignment, a declined/expired replacement offer returns to the Operator.
            if (waitsForOperator)
            {
                if (await NeedsOperator(task, "The replacement Staff path did not complete the task.", ct)) changedTasks.Add(task.TaskId);
                continue;
            }
            if (await Advance(task, null, ct)) changedTasks.Add(task.TaskId);
        }
        return changedTasks.Count;
    }

    public async Task<(int IntervalSec, int MaxAttempts)> Retry(CancellationToken ct)
    {
        var settings = await store.List<SystemSetting>(s => s.SettingKey == TaskRules.RetryIntervalSetting, ct);
        var interval = PositiveInt(settings, TaskRules.RetryIntervalSetting, TaskRules.DefaultRetryIntervalSec);
        return (interval, TaskDispatchScope.Automatic.Length);
    }

    private static int PositiveInt(IEnumerable<SystemSetting> settings, string key, int fallback)
    {
        var value = LatestInt(settings, key);
        return value is > 0 ? value.Value : fallback;
    }
    private static int? LatestInt(IEnumerable<SystemSetting> settings, string key)
        => int.TryParse(settings.Where(s => s.SettingKey == key).OrderByDescending(s => s.UpdatedAt).FirstOrDefault()?.SettingValue, out var value) ? value : null;

    /// <summary>Advances through empty topology rings immediately and opens the first ring with eligible Staff.</summary>
    public async Task<bool> Advance(WorkTask task, Guid? cameraId, CancellationToken ct)
    {
        if (task.Status != WorkTaskStatus.Offered || task.CurrentAssigneeUserId is not null) return false;
        var now = clock.UtcNow;
        var offers = await store.List<TaskOffer>(a => a.TaskId == task.TaskId, ct);
        if (offers.Any(o => o.Status == TaskOfferStatus.Pending && o.ExpiresAt > now)) return true;
        var timeout = await OfferTimeout(task, ct);
        var rejectedTwice = (await store.List<TaskAssignmentHistory>(a => a.TaskId == task.TaskId
            && TaskEvidenceRules.ReassignmentExclusionReasons.Contains(a.Reason!), ct))
            .Select(a => a.StaffUserId).ToHashSet();
        var previouslyOffered = offers.Where(o => o.DispatchScope != TaskDispatchScope.Manual).Select(o => o.StaffUserId).ToHashSet();
        var markers = await store.List<Notification>(n => n.TaskId == task.TaskId && n.NotificationType == TaskNotifier.DispatchRing, ct);
        var completedWaves = Math.Max(markers.Count, offers.Where(o => o.DispatchScope != TaskDispatchScope.Manual).Select(o => o.WaveNumber).DefaultIfEmpty(0).Max());
        for (var wave = completedWaves + 1; wave <= TaskDispatchScope.Automatic.Length; wave++)
        {
            var scope = TaskDispatchScope.Automatic[wave - 1];
            await notifier.Send(TaskNotifier.DispatchRing, $"Dispatch ring {wave}", scope, task, [], $"RING_{wave}", ct, cameraId);
            var zoneIds = await RingZoneIds(task.ZoneId, scope, ct);
            var candidates = (await roster.Eligible(task, now, zoneIds, ct))
                .Where(s => !previouslyOffered.Contains(s.User.UserId) && !rejectedTwice.Contains(s.User.UserId)).ToList();
            if (candidates.Count == 0) continue;
            var cycleId = Guid.NewGuid();
            foreach (var staff in candidates)
            {
                var offer = new TaskOffer { OfferId = Guid.NewGuid(), TaskId = task.TaskId, StaffUserId = staff.User.UserId,
                    DispatchCycleId = cycleId, WaveNumber = wave, DispatchScope = scope,
                    Status = TaskOfferStatus.Pending, OfferedAt = now, ExpiresAt = now.AddSeconds(timeout) };
                await store.Add(offer, ct);
                offers.Add(offer);
                previouslyOffered.Add(staff.User.UserId);
            }
            var recipients = candidates.Select(s => s.User.UserId).Distinct().ToList();
            await notifier.Send(TaskNotifier.Offer, $"{task.Priority} · New task", task.Title, task, recipients, scope, ct, cameraId);
            return true;
        }
        return await NeedsOperator(task, "No eligible Staff accepted the task after all Zone and Floor dispatch rings were exhausted.", ct);
    }

    private async Task<Guid[]> RingZoneIds(Guid taskZoneId, string scope, CancellationToken ct)
    {
        var target = await store.Find<Zone>(taskZoneId, ct);
        if (target is null) return [];
        if (scope == TaskDispatchScope.SameZone) return [target.ZoneId];
        var floor = await store.Find<Floor>(target.FloorId, ct);
        if (floor is null) return [];
        var sameFloor = await store.List<Zone>(z => z.FloorId == floor.FloorId && z.Status == "ACTIVE", ct);
        var links = await store.List<ZoneAdjacency>(a => a.ZoneId == target.ZoneId || a.AdjacentZoneId == target.ZoneId, ct);
        var adjacent = links.Select(a => a.ZoneId == target.ZoneId ? a.AdjacentZoneId : a.ZoneId).ToHashSet();
        if (scope == TaskDispatchScope.AdjacentSameFloor)
            return sameFloor.Where(z => adjacent.Contains(z.ZoneId)).Select(z => z.ZoneId).ToArray();
        if (scope == TaskDispatchScope.RemainingSameFloor)
            return sameFloor.Where(z => z.ZoneId != target.ZoneId && !adjacent.Contains(z.ZoneId)).Select(z => z.ZoneId).ToArray();

        var otherFloors = (await store.List<Floor>(f => f.SupermarketId == floor.SupermarketId && f.FloorId != floor.FloorId, ct)).ToList();
        if (otherFloors.Count == 0) return [];
        var nearestDistance = otherFloors.Min(f => Math.Abs((long)f.FloorNumber - floor.FloorNumber));
        var selectedFloors = scope == TaskDispatchScope.AdjacentFloor
            ? otherFloors.Where(f => Math.Abs((long)f.FloorNumber - floor.FloorNumber) == nearestDistance).Select(f => f.FloorId).ToHashSet()
            : otherFloors.Where(f => Math.Abs((long)f.FloorNumber - floor.FloorNumber) != nearestDistance).Select(f => f.FloorId).ToHashSet();
        return (await store.List<Zone>(z => selectedFloors.Contains(z.FloorId) && z.Status == "ACTIVE", ct)).Select(z => z.ZoneId).ToArray();
    }

    /// <summary>Rechecks that a Staff member still belongs to a Zone covered by the offer's dispatch ring.</summary>
    public async Task<bool> InScope(Guid taskZoneId, string scope, IReadOnlyCollection<Guid> staffZoneIds, CancellationToken ct)
        => (await RingZoneIds(taskZoneId, scope, ct)).Any(staffZoneIds.Contains);

    /// <summary>Notifies Operators once per task that it needs manual assignment.</summary>
    public async Task<bool> NeedsOperator(WorkTask task, string message, CancellationToken ct)
    {
        if ((await store.List<Notification>(n => n.TaskId == task.TaskId && n.NotificationType == TaskNotifier.NeedsAssignment, ct)).Count > 0) return false;
        await notifier.Send(TaskNotifier.NeedsAssignment, "Task needs manual assignment", $"{task.Title}: {message}", task, await notifier.Operators(ct), "OPERATOR", ct);
        return true;
    }

    public async Task<List<Guid>> CloseOpenOffers(Guid taskId, string status, string reason, DateTime now, CancellationToken ct, Guid? except = null)
    {
        var closed = new List<Guid>();
        foreach (var offer in await store.List<TaskOffer>(a => a.TaskId == taskId && a.Status == TaskOfferStatus.Pending, ct))
        {
            if (offer.OfferId == except) continue;
            offer.Status = status; offer.RespondedAt = now; offer.DeclineReasonCode = reason;
            await store.Update(offer, ct);
            await Audit(offer, status, reason, now, ct);
            closed.Add(offer.StaffUserId);
        }
        return closed;
    }

    private async Task<List<Guid>> OpenOfferStaff(Guid taskId, CancellationToken ct)
        => (await store.List<TaskOffer>(a => a.TaskId == taskId && a.Status == TaskOfferStatus.Pending, ct)).Select(a => a.StaffUserId).ToList();

    public async Task<int> OfferTimeout(WorkTask task, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(task.SlaSnapshotJson))
        {
            try
            {
                using var snapshot = JsonDocument.Parse(task.SlaSnapshotJson);
                if (snapshot.RootElement.ValueKind == JsonValueKind.Object
                    && snapshot.RootElement.TryGetProperty("offerTimeoutSec", out var value)
                    && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var seconds) && seconds > 0) return seconds;
            }
            catch (JsonException) { /* Older tasks can still use their policy or global retry setting. */ }
        }
        if (task.SlaPolicyId is { } policyId && await store.Find<SlaPolicy>(policyId, ct) is { OfferTimeoutSec: > 0 } policy)
            return policy.OfferTimeoutSec;
        return (await Retry(ct)).IntervalSec;
    }

    public async Task Audit(TaskOffer offer, string status, string? reason, DateTime now, CancellationToken ct)
    {
        if (offer.DispatchScope == TaskDispatchScope.Manual)
        {
            var manual = (await store.List<TaskAssignmentHistory>(a => a.TaskId == offer.TaskId && a.StaffUserId == offer.StaffUserId
                && a.AssignmentType == TaskAssignmentType.Manual && a.Status == OfferStatus.Pending && a.EndedAt == null, ct))
                .OrderByDescending(a => a.AssignedAt).FirstOrDefault();
            if (manual is not null)
            {
                manual.Status = status; manual.RespondedAt = now;
                if (status != OfferStatus.Accepted) manual.EndedAt = now;
                await store.Update(manual, ct);
                return;
            }
        }
        await store.Add(new TaskAssignmentHistory
        {
            TaskAssignmentId = Guid.NewGuid(), TaskId = offer.TaskId, StaffUserId = offer.StaffUserId,
            AssignmentType = TaskAssignmentType.Offer, Status = status, AssignedAt = offer.OfferedAt,
            RespondedAt = now, EndedAt = status == OfferStatus.Accepted ? null : now, Reason = reason
        }, ct);
    }

    private async Task<(SlaPolicy Policy, SlaPolicyAction Action)?> TaskAction(OperationalEvent ev, CancellationToken ct)
    {
        if (ev.RuleId is not { } ruleId) return null;
        var rule = await store.Find<MonitoringRule>(ruleId, ct);
        if (rule is null) return null;
        var configuration = await store.Find<MonitoringConfiguration>(rule.ConfigId, ct);
        if (configuration is null) return null;
        if (!ResponsePolicyRules.IsOperationalEventRule(rule) || rule.EventTypeId != ev.EventTypeId
            || !rule.Enabled || configuration.Status != "ACTIVE") return null;
        var policies = await store.List<SlaPolicy>(p => p.ProblemDomain == "OPERATIONAL_EVENT" && p.EventTypeId == ev.EventTypeId && p.IsEnabled, ct);
        if (policies.Count != 1)
        {
            await ConfigurationException(ev, policies.Count == 0 ? "POLICY_MISSING" : "POLICY_AMBIGUOUS", ct);
            return null;
        }
        var policy = policies[0];
        if (!OperationalSlaPolicyRules.Valid(policy))
        {
            await ConfigurationException(ev, "POLICY_INVALID", ct);
            return null;
        }
        var actions = await store.List<SlaPolicyAction>(a => a.PolicyId == policy.PolicyId && a.IsEnabled, ct);
        var matches = actions.Where(a => OperationalSlaPolicyRules.AlertLevel(a.ConditionJson) == ev.Priority).ToList();
        var expected = ev.Priority == "WARNING" ? OperationalSlaPolicyRules.NotifyOperator : OperationalSlaPolicyRules.CreateTask;
        if (ev.Priority is not ("WARNING" or "CRITICAL") || matches.Count != 1
            || matches[0].ActionType != expected || matches[0].ExecutionMode != "AUTOMATIC")
        {
            await ConfigurationException(ev, "ACTION_MISSING_INVALID_OR_AMBIGUOUS", ct);
            return null;
        }
        return (policy, matches[0]);
    }

    private Task ConfigurationException(OperationalEvent ev, string code, CancellationToken ct)
        => EventNotice(ev, "OPERATIONAL_SLA_CONFIGURATION_EXCEPTION", "Operational SLA configuration needs attention", code, ct);

    private async Task EventNotice(OperationalEvent ev, string type, string title, string message, CancellationToken ct, string? roleName = null)
    {
        // Runs within the event writer's serializable transaction: one notice per episode/type.
        if ((await store.List<Notification>(n => n.EventId == ev.EventId && n.NotificationType == type, ct)).Count != 0) return;
        var role = roleName ?? "OPERATOR";
        await notifier.Broadcast(type, title, message, await notifier.RoleUsers(role, ct), role, ct, ev.EventId, ev.Priority, ev.CameraId);
    }

    private static TaskParameters? Parameters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 32768) return null;
        try { return JsonSerializer.Deserialize<TaskParameters>(json, Json); }
        catch (JsonException) { return null; }
    }

    private async Task<TaskType?> ResolveTaskType(string? configuredCode, OperationalEventType? eventType, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(configuredCode))
        {
            var code = configuredCode.Trim().ToUpperInvariant();
            var matches = await store.List<TaskType>(t => t.Code == code, ct);
            return matches.Count == 1 ? matches[0] : null;
        }
        // Source-category labels cannot distinguish TT-03 from TT-04. Require an explicit mapping.
        return null;
    }
}
