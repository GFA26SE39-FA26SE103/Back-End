using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
using AppError = Supermarket.Application.ApplicationException;
namespace Supermarket.Tests;

/// <summary>MF-03 completion: Staff evidence and submission, Operator verification and rework (Report 3 §3.7.4–3.7.5, BR-25–BR-28).</summary>
public sealed class TaskEvidenceTests
{
    // 10:00 store time (UTC+7); the day shift runs 08:00-16:00 local.
    private static readonly DateTime Now = new(2026, 10, 11, 3, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] Jpeg = [0xff, 0xd8, 0xff, 0xe0, 1, 2, 3, 4];

    [Fact]
    public async Task AnOperationalTaskIsSubmittedWithItsNoteAndWaitsForTheOperator()
    {
        var w = new World(); var a = w.Staff("An"); var op = w.User("OPERATOR", "Olivia");
        var task = await w.InProgress(a);
        var staff = w.Evidence(a);

        Assert.Equal("INVALID_FIELD", (await Assert.ThrowsAsync<DomainException>(() => staff.Submit(task.TaskId, new(" "), default))).Code);
        var draft = await w.Upload(a, task, TaskEvidenceType.AfterPhoto);
        Assert.Single(draft.Drafts);
        Assert.False(draft.RequiresAfterPhoto);

        var result = await staff.Submit(task.TaskId, new("Opened counter 4; queue cleared"), default);
        Assert.Equal((WorkTaskStatus.Submitted, (DateTime?)Now), (result.Status, result.SubmittedAt));
        var submission = Assert.Single(result.Evidence.Submissions);
        Assert.Equal(("Opened counter 4; queue cleared", TaskEvidenceReview.Pending, 1), (submission.Note, submission.ReviewStatus, submission.Photos.Length));
        Assert.Empty(result.Evidence.Drafts);
        Assert.Equal([op], w.Recipients(Assert.Single(w.Notifications(task, TaskEvidenceWork.Submitted))));
        // Retrying a submission that already went through changes nothing.
        Assert.Equal(WorkTaskStatus.Submitted, (await staff.Submit(task.TaskId, new("again"), default)).Status);
        Assert.Single(w.Store.Values.OfType<TaskEvidence>(), e => e.EvidenceType == TaskEvidenceType.CompletionNote);
    }

    [Fact]
    public async Task OnlyTheAssigneeAddsEvidenceAndOnlyWhileTheTaskIsInProgress()
    {
        var w = new World(); var a = w.Staff("An"); var b = w.Staff("Binh");
        var task = await w.Accepted(a);
        Assert.Equal("TASK_NOT_IN_PROGRESS", (await Assert.ThrowsAsync<AppError>(() => w.Upload(a, task, TaskEvidenceType.AfterPhoto))).Code);
        Assert.Equal("TASK_NOT_IN_PROGRESS", (await Assert.ThrowsAsync<AppError>(() => w.Evidence(a).Submit(task.TaskId, new("Done"), default))).Code);
        await w.StaffTasks(a).Start(task.TaskId, default);
        Assert.Equal("NOT_FOUND", (await Assert.ThrowsAsync<AppError>(() => w.Upload(b, task, TaskEvidenceType.AfterPhoto))).Code);
        Assert.Equal("NOT_FOUND", (await Assert.ThrowsAsync<AppError>(() => w.Evidence(b).Get(task.TaskId, default))).Code);
        Assert.Equal("INVALID_STATUS", (await Assert.ThrowsAsync<DomainException>(() => w.Upload(a, task, TaskEvidenceType.CompletionNote))).Code);
        Assert.Empty(w.Files.Saved);
    }

    [Fact]
    public async Task AHazardIncidentTaskNeedsBeforeAndAfterPhotosAndARetriedUploadReplacesTheDraft()
    {
        var w = new World(); var a = w.Staff("An");
        var task = await w.InProgress(a, incidentRequiresBefore: true);
        var staff = w.Evidence(a);

        var first = await w.Upload(a, task, TaskEvidenceType.AfterPhoto);
        Assert.True(first.RequiresBeforePhoto && first.RequiresAfterPhoto);
        var retried = await w.Upload(a, task, TaskEvidenceType.AfterPhoto);
        Assert.Single(retried.Drafts);
        Assert.Single(w.Files.Deleted);
        Assert.Equal("EVIDENCE_REQUIRED", (await Assert.ThrowsAsync<AppError>(() => staff.Submit(task.TaskId, new("Mopped"), default))).Code);

        var withBefore = await w.Upload(a, task, TaskEvidenceType.BeforePhoto);
        var removed = await staff.RemoveDraft(task.TaskId, withBefore.Drafts.Single(d => d.EvidenceType == TaskEvidenceType.BeforePhoto).EvidenceId, default);
        Assert.Single(removed.Drafts);
        await w.Upload(a, task, TaskEvidenceType.BeforePhoto);
        Assert.Equal(WorkTaskStatus.Submitted, (await staff.Submit(task.TaskId, new("Mopped and dried"), default)).Status);
    }

    [Fact]
    public async Task TheOperatorVerifiesSubmittedWorkAndTheStaffMemberIsFreedForNewTasks()
    {
        var w = new World(); var a = w.Staff("An"); var op = w.User("OPERATOR", "Olivia");
        var task = await w.InProgress(a);
        Assert.Equal("TASK_NOT_SUBMITTED", (await Assert.ThrowsAsync<AppError>(() => w.Evidence(op).Verify(task.TaskId, new(), default))).Code);
        await w.Upload(a, task, TaskEvidenceType.AfterPhoto);
        await w.Evidence(a).Submit(task.TaskId, new("Done"), default);

        Assert.Equal("FORBIDDEN", (await Assert.ThrowsAsync<AppError>(() => w.Evidence(a).Verify(task.TaskId, new(), default))).Code);
        Assert.Equal("TASK_CHANGED", (await Assert.ThrowsAsync<AppError>(() => w.Evidence(op).Verify(task.TaskId, new(Now.AddDays(-1)), default))).Code);
        var verified = await w.Evidence(op).Verify(task.TaskId, new(), default);

        Assert.Equal((WorkTaskStatus.Verified, (DateTime?)Now), (verified.Status, verified.VerifiedAt));
        Assert.Equal(op, task.VerifiedByUserId);
        Assert.All(w.Store.Values.OfType<TaskEvidence>(), e => Assert.Equal(TaskEvidenceReview.Verified, e.ReviewStatus));
        Assert.Equal([a], w.Recipients(Assert.Single(w.Notifications(task, TaskEvidenceWork.Verified))));
        Assert.Equal(0, (await w.StaffTasks(a).Eligibility(default)).ActiveTasks);
        Assert.Single(await w.StaffTasks(a).Tasks("VERIFIED", default));
        // The Staff member can still open the photo; a viewer outside the Task cannot.
        var photo = verified.Evidence.Submissions.Single().Photos.Single();
        Assert.Equal("image/jpeg", (await w.Evidence(a).Image(task.TaskId, photo.EvidenceId, default)).ContentType);
        Assert.Equal("NOT_FOUND", (await Assert.ThrowsAsync<AppError>(() => w.Evidence(w.Staff("Chi")).Image(task.TaskId, photo.EvidenceId, default))).Code);
    }

    [Fact]
    public async Task ARejectionReturnsTheTaskForReworkAndTheSecondSendsItToAnotherStaffMember()
    {
        var w = new World(); var a = w.Staff("An"); var b = w.Staff("Binh"); var op = w.User("OPERATOR", "Olivia");
        var task = await w.InProgress(a);
        await w.Evidence(a).Submit(task.TaskId, new("Done"), default);
        Assert.Equal("INVALID_FIELD", (await Assert.ThrowsAsync<DomainException>(() => w.Evidence(op).Reject(task.TaskId, new(""), default))).Code);

        var rework = await w.Evidence(op).Reject(task.TaskId, new("The queue is still long"), default);
        Assert.Equal((WorkTaskStatus.InProgress, 1, a), (rework.Status, rework.RejectionCount, rework.AssigneeUserId));
        Assert.Equal(("The queue is still long", TaskEvidenceReview.Rejected), (rework.Evidence.Submissions.Single().RejectReason, rework.Evidence.Submissions.Single().ReviewStatus));
        Assert.Equal([a], w.Recipients(Assert.Single(w.Notifications(task, TaskEvidenceWork.Rejected))));

        w.Clock.UtcNow = Now.AddMinutes(5);
        await w.Upload(a, task, TaskEvidenceType.AfterPhoto);
        await w.Evidence(a).Submit(task.TaskId, new("Opened two counters"), default);
        var second = await w.Evidence(op).Reject(task.TaskId, new("Still not cleared"), default);

        Assert.Equal((WorkTaskStatus.Offered, 2, (Guid?)null), (second.Status, second.RejectionCount, second.AssigneeUserId));
        Assert.Equal(2, second.Evidence.Submissions.Length);
        Assert.Single(second.Evidence.Submissions[0].Photos);
        Assert.Equal([op], w.Recipients(Assert.Single(w.Notifications(task, TaskEvidenceWork.ReassignmentRequired))));
        Assert.True((await w.Operator(op).Get(task.TaskId, default)).NeedsAssignment);
        // The retry worker leaves it to the Operator, who cannot give it back to the same Staff member.
        w.Clock.UtcNow = Now.AddHours(1); await w.Dispatch.Tick(default);
        Assert.Empty(await w.StaffTasks(a).Offers(true, default));
        Assert.DoesNotContain(await w.Operator(op).AssignableStaff(task.TaskId, default), s => s.UserId == a);
        Assert.Equal("STAFF_REJECTED_TWICE", (await Assert.ThrowsAsync<AppError>(() => w.Operator(op).Assign(task.TaskId, new(a, "Retry"), default))).Code);
        var reassigned = await w.Operator(op).Assign(task.TaskId, new(b, null), default);
        Assert.Equal((WorkTaskStatus.Accepted, b), (reassigned.Status, reassigned.AssigneeUserId));
        // The former performer still sees their own submissions, the new one starts clean.
        Assert.Equal(2, (await w.Evidence(a).Get(task.TaskId, default)).Submissions.Length);
        Assert.Empty((await w.Evidence(b).Get(task.TaskId, default)).Submissions);
    }

    private sealed class World
    {
        public MonitoringSetupTests.MemoryStore Store { get; } = new();
        public StaffTaskTests.Clock Clock { get; } = new() { UtcNow = Now };
        public MemoryFiles Files { get; } = new();
        public TaskDispatch Dispatch { get; }
        private readonly StoreCalendar calendar = new(StoreCalendar.Zone(null));
        private readonly Zone zone;
        private readonly Schema schema = new();
        private readonly StaffRoster roster;
        private readonly TaskNotifier notifier;
        private readonly TaskViews views;
        private readonly Guid shiftId = Guid.NewGuid();

        public World()
        {
            zone = new Zone { ZoneId = Guid.NewGuid(), FloorId = Guid.NewGuid(), Code = "B", Name = "Zone B · Checkout" };
            foreach (var role in new[] { "ADMIN", "OPERATOR", "MANAGER", "STAFF" }) Store.Values.Add(new Role { RoleId = Guid.NewGuid(), Name = role });
            Store.Values.AddRange([zone, new Shift { ShiftId = shiftId, Name = "Morning", StartTime = new(8, 0), EndTime = new(16, 0) }]);
            roster = new StaffRoster(Store, calendar);
            notifier = new TaskNotifier(Store, Clock);
            Dispatch = new TaskDispatch(Store, roster, notifier, Clock, schema);
            views = new TaskViews(Store, Dispatch);
        }

        public Guid User(string role, string name)
        {
            var user = new UserAccount { UserId = Guid.NewGuid(), RoleId = Store.Values.OfType<Role>().Single(r => r.Name == role).RoleId, FullName = name, Email = $"{name}@test", Status = "ACTIVE" };
            Store.Values.Add(user);
            return user.UserId;
        }

        public Guid Staff(string name)
        {
            var id = User("STAFF", name);
            var assignment = new ShiftAssignment { ShiftAssignmentId = Guid.NewGuid(), UserId = id, ShiftId = shiftId, WorkDate = new(2026, 10, 11), CheckInAt = Now.AddHours(-2) };
            Store.Values.Add(assignment);
            Store.Values.Add(new StaffZoneAssignment { StaffZoneAssignmentId = Guid.NewGuid(), ShiftAssignmentId = assignment.ShiftAssignmentId, ZoneId = zone.ZoneId, AssignedAt = Now.AddHours(-3) });
            return id;
        }

        /// <summary>A Task the Staff member accepted for later, as after Handle later.</summary>
        public Task<WorkTask> Accepted(Guid staff, bool? incidentRequiresBefore = null)
        {
            Guid? incidentId = null;
            if (incidentRequiresBefore is { } before)
            {
                var type = new IncidentType { IncidentTypeId = Guid.NewGuid(), Code = "SPILL", Name = "Spill", RequiresBeforePhoto = before };
                var incident = new Incident { IncidentId = Guid.NewGuid(), IncidentTypeId = type.IncidentTypeId, ZoneId = zone.ZoneId, Title = "Spill at entrance" };
                Store.Values.AddRange([type, incident]);
                incidentId = incident.IncidentId;
            }
            var task = new WorkTask { TaskId = Guid.NewGuid(), ZoneId = zone.ZoneId, Title = "Clear the queue", Priority = "WARNING", Status = WorkTaskStatus.Accepted,
                OriginType = incidentId is null ? TaskOrigin.OperationalEvent : TaskOrigin.Incident, IncidentId = incidentId,
                CurrentAssigneeUserId = staff, AssignedAt = Now.AddMinutes(-10), AcceptedAt = Now.AddMinutes(-10), CreatedAt = Now.AddMinutes(-11) };
            Store.Values.Add(task);
            Store.Values.Add(new TaskAssignmentHistory { TaskAssignmentId = Guid.NewGuid(), TaskId = task.TaskId, StaffUserId = staff, AssignmentType = TaskAssignmentType.Offer,
                Status = OfferStatus.Accepted, AssignedAt = Now.AddMinutes(-11), RespondedAt = Now.AddMinutes(-10) });
            return Task.FromResult(task);
        }

        public async Task<WorkTask> InProgress(Guid staff, bool? incidentRequiresBefore = null)
        {
            var task = await Accepted(staff, incidentRequiresBefore);
            await StaffTasks(staff).Start(task.TaskId, default);
            return Store.Values.OfType<WorkTask>().Single(t => t.TaskId == task.TaskId);
        }

        public Task<TaskEvidenceView> Upload(Guid staff, WorkTask task, string type)
            => Evidence(staff).Upload(task.TaskId, type, new MemoryStream(Jpeg), "photo.jpg", "image/jpeg", Jpeg.Length, default);

        public List<Notification> Notifications(WorkTask task, string type) => Store.Values.OfType<Notification>().Where(n => n.TaskId == task.TaskId && n.NotificationType == type).ToList();
        public Guid[] Recipients(Notification n) => Store.Values.OfType<NotificationRecipient>().Where(r => r.NotificationId == n.NotificationId).Select(r => r.UserId).ToArray();

        public TaskEvidenceWork Evidence(Guid user) => new(Store, Current(user), Clock, notifier, schema, Files);
        public StaffTasks StaffTasks(Guid user) => new(Store, Current(user), Clock, calendar, roster, Dispatch, views, schema);
        public OperatorTasks Operator(Guid user) => new(Store, Current(user), Clock, roster, Dispatch, notifier, views, schema, new StaffTaskTests.OperatorQueries(Store));

        private User Current(Guid id)
        {
            var user = Store.Values.OfType<UserAccount>().Single(u => u.UserId == id);
            return new User(id, Store.Values.OfType<Role>().Single(r => r.RoleId == user.RoleId).Name);
        }
    }

    internal sealed class MemoryFiles : ITaskEvidenceStorage
    {
        public Dictionary<string, (byte[] Bytes, string Type)> Saved { get; } = [];
        public List<string> Deleted { get; } = [];
        public async Task<StoredEvidence> Save(Guid taskId, Stream content, string filename, string contentType, long length, CancellationToken ct)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            var token = Guid.NewGuid().ToString("N") + ".jpg";
            Saved[token] = (copy.ToArray(), contentType);
            return new(token, contentType);
        }
        public Task<EvidenceFile?> Open(Guid taskId, string token, CancellationToken ct)
            => Task.FromResult(Saved.TryGetValue(token, out var file) ? new EvidenceFile(new MemoryStream(file.Bytes), file.Type) : null);
        public Task Delete(Guid taskId, string token, CancellationToken ct)
        {
            Deleted.Add(token);
            Saved.Remove(token);
            return Task.CompletedTask;
        }
    }

    private sealed record User(Guid UserId, string Role) : ICurrentUser;
    private sealed class Schema : ITaskSchema { public Task<bool> Ready(CancellationToken ct) => Task.FromResult(true); }
}
