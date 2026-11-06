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
    {
        var taskType = task.TaskTypeId is { } taskTypeId ? await store.Find<TaskType>(taskTypeId, ct) : null;
        if (taskType is null) return [];
        return (await OnShift(now, ct)).Where(s => s.ZoneIds.Contains(task.ZoneId)
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
    public const string Offer = "TASK_OFFER", OfferWithdrawn = "TASK_OFFER_WITHDRAWN", Assigned = "TASK_ASSIGNED", NeedsAssignment = "TASK_NEEDS_ASSIGNMENT",
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
/// One episode yields at most one Task; a priority rise updates it.
/// </summary>
public sealed class TaskDispatch(ISetupStore store, StaffRoster roster, TaskNotifier notifier, IClock clock, ITaskSchema schema) : IOperationalEventResponder
{
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
        {
            if (TaskRules.Rank(ev.Priority) > TaskRules.Rank(existing.Priority))
            {
                existing.Priority = ev.Priority;
                await store.Update(existing, ct);
                var to = existing.CurrentAssigneeUserId is { } assignee ? [assignee] : await OpenOfferStaff(existing.TaskId, ct);
                await notifier.Send(TaskNotifier.PriorityRaised, $"Priority raised to {ev.Priority}", $"{existing.Title}: the event is now {ev.Priority}.", existing, to, "TASK_PARTICIPANT", ct, ev.CameraId);
            }
            return;
        }
        var match = await TaskAction(ev, ct);
        if (match is null) return;
        var (policy, action) = match.Value;
        if (action.ActionType == OperationalSlaPolicyRules.NotifyOperator)
        {
            await EventNotice(ev, "OPERATIONAL_EVENT_WARNING", "Operational warning", "Review the operational event.", ct);
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
            Priority = ev.Priority, Status = WorkTaskStatus.Offered, OriginType = TaskOrigin.OperationalEvent, EventId = ev.EventId, TaskTypeId = taskType.TaskTypeId,
            SlaPolicyId = policy.PolicyId, SlaSnapshotJson = JsonSerializer.Serialize(snapshot, Json),
            ResponseDeadlineAt = snapshot.ResponseDeadlineAt, ResolutionDeadlineAt = snapshot.ResolutionDeadlineAt };
        await store.Add(task, ct);
        await OfferRound(task, ev.CameraId, ct);
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
                await notifier.Send(TaskNotifier.EventCleared, "Event cleared", $"{task.Title}: the camera shows the area back to normal. Check the area and finish the task.", task, [assignee], "ASSIGNEE", ct, ev.CameraId);
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
        var now = clock.UtcNow; var (_, maxAttempts) = await Retry(ct); var changedTasks = new HashSet<Guid>();
        foreach (var task in await store.List<WorkTask>(t => t.Status == WorkTaskStatus.Offered && t.CurrentAssigneeUserId == null, ct))
        {
            // A Task rejected twice waits for the Operator to reassign it (BR-28); it is not re-offered.
            if ((await store.List<TaskAssignmentHistory>(a => a.TaskId == task.TaskId && a.Reason == TaskEvidenceRules.RejectedTwiceReason, ct)).Count > 0) continue;
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
            if (offers.Any(o => o.Status == TaskOfferStatus.Pending && o.ExpiresAt > now)) continue;
            var rounds = await store.List<Notification>(n => n.TaskId == task.TaskId && n.NotificationType == TaskNotifier.Offer, ct);
            if (rounds.Count == 0) continue;
            var timeout = await OfferTimeout(task, ct);
            if (now < rounds.Max(n => n.CreatedAt).AddSeconds(timeout)) continue;
            if (rounds.Count < maxAttempts)
            {
                await OfferRound(task, null, ct);
                changedTasks.Add(task.TaskId);
                continue;
            }
            if (await NeedsOperator(task, "Nobody accepted the task after every TaskOffer dispatch cycle.", ct)) changedTasks.Add(task.TaskId);
        }
        return changedTasks.Count;
    }

    public async Task<(int IntervalSec, int MaxAttempts)> Retry(CancellationToken ct)
    {
        var keys = new[] { TaskRules.RetryIntervalSetting, TaskRules.MaxRetryCountSetting };
        var settings = await store.List<SystemSetting>(s => keys.Contains(s.SettingKey), ct);
        var interval = PositiveInt(settings, TaskRules.RetryIntervalSetting, TaskRules.DefaultRetryIntervalSec);
        var retries = NonNegativeInt(settings, TaskRules.MaxRetryCountSetting, TaskRules.DefaultMaxAttempts - 1);
        // max_retry_count counts re-notifications after the first offer.
        return (interval, 1 + retries);
    }

    private static int PositiveInt(IEnumerable<SystemSetting> settings, string key, int fallback)
    {
        var value = LatestInt(settings, key);
        return value is > 0 ? value.Value : fallback;
    }
    private static int NonNegativeInt(IEnumerable<SystemSetting> settings, string key, int fallback)
    {
        var value = LatestInt(settings, key);
        return value is >= 0 ? value.Value : fallback;
    }
    private static int? LatestInt(IEnumerable<SystemSetting> settings, string key)
        => int.TryParse(settings.Where(s => s.SettingKey == key).OrderByDescending(s => s.UpdatedAt).FirstOrDefault()?.SettingValue, out var value) ? value : null;

    /// <summary>Creates one V7 SAME_ZONE dispatch cycle. Active duplicate task/staff offers are forbidden.</summary>
    private async Task OfferRound(WorkTask task, Guid? cameraId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var timeout = await OfferTimeout(task, ct);
        var cycleId = Guid.NewGuid();
        var offers = await store.List<TaskOffer>(a => a.TaskId == task.TaskId, ct);
        var rejectedTwice = (await store.List<TaskAssignmentHistory>(a => a.TaskId == task.TaskId && a.Reason == TaskEvidenceRules.RejectedTwiceReason, ct))
            .Select(a => a.StaffUserId).ToHashSet();
        foreach (var staff in await roster.Eligible(task, now, ct))
        {
            if (rejectedTwice.Contains(staff.User.UserId)
                || offers.Any(o => o.StaffUserId == staff.User.UserId && o.Status is TaskOfferStatus.Pending or TaskOfferStatus.Declined)) continue;
            var offer = new TaskOffer { OfferId = Guid.NewGuid(), TaskId = task.TaskId, StaffUserId = staff.User.UserId,
                DispatchCycleId = cycleId, WaveNumber = 1, DispatchScope = TaskDispatchScope.SameZone,
                Status = TaskOfferStatus.Pending, OfferedAt = now, ExpiresAt = now.AddSeconds(timeout) };
            await store.Add(offer, ct);
            offers.Add(offer);
        }
        var open = offers.Where(o => o.Status == TaskOfferStatus.Pending && o.ExpiresAt > now).Select(o => o.StaffUserId).Distinct().ToList();
        await notifier.Send(TaskNotifier.Offer, $"{task.Priority} · New task", task.Title, task, open, "ELIGIBLE_ZONE_STAFF", ct, cameraId);
        if (open.Count == 0)
            await NeedsOperator(task, "No eligible Staff has the required specialty, current check-in, Zone assignment and workload capacity.", ct);
    }

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

    private async Task EventNotice(OperationalEvent ev, string type, string title, string message, CancellationToken ct)
    {
        // Runs within the event writer's serializable transaction: one notice per episode/type.
        if ((await store.List<Notification>(n => n.EventId == ev.EventId && n.NotificationType == type, ct)).Count != 0) return;
        await notifier.Broadcast(type, title, message, await notifier.Operators(ct), "OPERATOR", ct, ev.EventId, ev.Priority, ev.CameraId);
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
