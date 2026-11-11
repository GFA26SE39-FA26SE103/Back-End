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

    [Theory]
    [InlineData(TaskOrigin.OperationalEvent, "CRITICAL", "MEDIUM", "TT-01", TaskVerificationMode.Auto)]
    [InlineData(TaskOrigin.OperationalEvent, "CRITICAL", "CRITICAL", "TT-01", TaskVerificationMode.Manual)]
    [InlineData(TaskOrigin.Incident, "HIGH", "HIGH", "TT-01", TaskVerificationMode.Manual)]
    [InlineData(TaskOrigin.OperationalEvent, "CRITICAL", "HIGH", "TT-07", TaskVerificationMode.Manual)]
    [InlineData(TaskOrigin.OperationalEvent, "CRITICAL", "HIGH", "TT-12", TaskVerificationMode.Manual)]
    [InlineData(TaskOrigin.OperationalEvent, "CRITICAL", "HIGH", "TT-15", TaskVerificationMode.Manual)]
    [InlineData(TaskOrigin.OperationalEvent, "CRITICAL", "HIGH", "TT-06", TaskVerificationMode.Manual)]
    [InlineData(TaskOrigin.OperationalEvent, "CRITICAL", null, "TT-01", TaskVerificationMode.Manual)]
    public void VerificationLaneFailsSafeForOriginSlaPriorityAndTaskType(string origin, string taskPriority, string? slaPriority, string code, string expected)
    {
        var task = new WorkTask { OriginType = origin, Priority = taskPriority };
        TaskVerificationRules.Initialize(task, new TaskType { Code = code }, slaPriority);
        Assert.Equal(expected, task.VerificationMode);
        Assert.Equal(expected == TaskVerificationMode.Auto ? TaskVerificationMethod.OperationalMetric : TaskVerificationMethod.OperatorEvidence,
            task.VerificationMethod);
    }

    [Fact]
    public void CriticalAlertLevelAloneDoesNotForceManualWhenSlaPriorityIsHigh()
    {
        var task = new WorkTask { OriginType = TaskOrigin.OperationalEvent, Priority = "CRITICAL" };
        TaskVerificationRules.Initialize(task, new TaskType { Code = "TT-01" }, "HIGH");
        Assert.Equal((TaskVerificationMode.Auto, TaskVerificationMethod.OperationalMetric),
            (task.VerificationMode, task.VerificationMethod));
    }

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
    public async Task MetricBackedOperationalTaskAutoVerifiesOnlyAfterAValidRecovery()
    {
        var w = new World(); var a = w.Staff("An"); var op = w.User("OPERATOR", "Olivia");
        var task = await w.InProgress(a); var ev = w.AutoEvent(task, cleared: false, validMetric: true);

        var submitted = await w.Evidence(a).Submit(task.TaskId, new("Opened another checkout"), default);
        Assert.Equal((WorkTaskStatus.Submitted, TaskVerificationMode.Auto, TaskVerificationMethod.OperationalMetric),
            (submitted.Status, submitted.Evidence.VerificationMode, submitted.Evidence.VerificationMethod));
        Assert.Equal("AUTO_VERIFICATION_PENDING", (await Assert.ThrowsAsync<AppError>(() => w.Evidence(op).Verify(task.TaskId, new(), default))).Code);
        Assert.Empty(w.Notifications(task, TaskEvidenceWork.Submitted));

        w.Clock.UtcNow = Now.AddMinutes(1);
        ev.Status = "CLEARED"; ev.ClearedAt = w.Clock.UtcNow; ev.LastObservedAt = w.Clock.UtcNow; ev.MetricValue = 1;
        ev.MetadataJson = World.MetricJson(1, valid: true);
        await w.Dispatch.Cleared(ev, default);

        var verified = await w.Evidence(a).Get(task.TaskId, default);
        Assert.Equal((WorkTaskStatus.Verified, TaskVerificationMode.Auto, TaskVerificationMethod.OperationalMetric),
            (verified.TaskStatus, verified.VerificationMode, verified.VerificationMethod));
        Assert.Null(task.VerifiedByUserId);
        Assert.Equal(w.Clock.UtcNow, task.VerifiedAt);
        Assert.All(w.Store.Values.OfType<TaskEvidence>(), row => Assert.Equal(TaskEvidenceReview.Verified, row.ReviewStatus));
        Assert.Equal([a], w.Recipients(Assert.Single(w.Notifications(task, TaskVerification.AutoVerified))));
    }

    [Fact]
    public async Task MissingOrInvalidAutoMetricFallsBackToManualOperatorVerification()
    {
        var w = new World(); var a = w.Staff("An"); var op = w.User("OPERATOR", "Olivia");
        var task = await w.InProgress(a); w.AutoEvent(task, cleared: false, validMetric: false);

        var submitted = await w.Evidence(a).Submit(task.TaskId, new("Completed"), default);

        Assert.Equal((WorkTaskStatus.Submitted, TaskVerificationMode.Manual, TaskVerificationMethod.MetricException),
            (submitted.Status, submitted.Evidence.VerificationMode, submitted.Evidence.VerificationMethod));
        Assert.Equal([op], w.Recipients(Assert.Single(w.Notifications(task, TaskVerification.VerificationException))));
        Assert.Equal(WorkTaskStatus.Verified, (await w.Evidence(op).Verify(task.TaskId, new(), default)).Status);
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
    public async Task AnIncidentTaskCanBeSubmittedWithANoteAndNoPhotos()
    {
        var w = new World(); var a = w.Staff("An"); var op = w.User("OPERATOR", "Olivia");
        var task = await w.InProgress(a, incidentRequiresBefore: true);
        var staff = w.Evidence(a);

        var evidence = await staff.Get(task.TaskId, default);
        Assert.False(evidence.RequiresBeforePhoto);
        Assert.False(evidence.RequiresAfterPhoto);

        Assert.Equal("MISSING_PHOTO_REASON_REQUIRED", (await Assert.ThrowsAsync<DomainException>(() =>
            staff.Submit(task.TaskId, new("Mopped and dried"), default))).Code);
        var result = await staff.Submit(task.TaskId,
            new("Mopped and dried", MissingPhotoReason: "The aisle camera was blocked by the cleanup barrier"), default);
        Assert.Equal(WorkTaskStatus.Submitted, result.Status);
        var submission = Assert.Single(result.Evidence.Submissions);
        Assert.Empty(submission.Photos);
        Assert.Equal("The aisle camera was blocked by the cleanup barrier", submission.MissingPhotoReason);
        Assert.Equal(WorkTaskStatus.Verified, (await w.Evidence(op).Verify(task.TaskId, new(), default)).Status);
        Assert.All(w.Store.Values.OfType<TaskEvidence>(), e => Assert.Equal(TaskEvidenceReview.Verified, e.ReviewStatus));
    }

    [Fact]
    public async Task AnIncidentTaskCanUseAnAfterPhotoInsteadOfANoteButCannotSubmitNeither()
    {
        var w = new World(); var a = w.Staff("An"); var op = w.User("OPERATOR", "Olivia");
        var task = await w.InProgress(a, incidentRequiresBefore: false);
        var staff = w.Evidence(a);

        Assert.Equal("EVIDENCE_REQUIRED", (await Assert.ThrowsAsync<AppError>(() => staff.Submit(task.TaskId, new(null), default))).Code);
        await w.Upload(a, task, TaskEvidenceType.AfterPhoto);

        var result = await staff.Submit(task.TaskId, new(" "), default);
        var submission = Assert.Single(result.Evidence.Submissions);
        Assert.Equal(WorkTaskStatus.Submitted, result.Status);
        Assert.Equal("", submission.Note);
        Assert.Equal(TaskEvidenceType.AfterPhoto, Assert.Single(submission.Photos).EvidenceType);
        Assert.Equal(WorkTaskStatus.Verified, (await w.Evidence(op).Verify(task.TaskId, new(), default)).Status);
    }

    [Fact]
    public async Task MissingPhotoReasonIsRejectedWhenAnAfterPhotoExistsOrTheTaskIsNotAnIncident()
    {
        var w = new World(); var a = w.Staff("An");
        var incident = await w.InProgress(a, incidentRequiresBefore: false);
        await w.Upload(a, incident, TaskEvidenceType.AfterPhoto);
        Assert.Equal("MISSING_PHOTO_REASON_NOT_APPLICABLE", (await Assert.ThrowsAsync<DomainException>(() => w.Evidence(a).Submit(
            incident.TaskId, new("Done", MissingPhotoReason: "Camera unavailable"), default))).Code);

        var routine = await w.InProgress(a);
        Assert.Equal("MISSING_PHOTO_REASON_NOT_APPLICABLE", (await Assert.ThrowsAsync<DomainException>(() => w.Evidence(a).Submit(
            routine.TaskId, new("Done", MissingPhotoReason: "No photo"), default))).Code);
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
    public async Task SecondRejectionAutomaticallyDispatchesAnotherStaffThenNextRejectionStopsAtOperator()
    {
        var w = new World(); var a = w.Staff("An"); var b = w.Staff("Binh", adjacent: true); var op = w.User("OPERATOR", "Olivia");
        var task = await w.InProgress(a);
        var deadlines = (task.ResponseDeadlineAt, task.ResolutionDeadlineAt, task.SlaSnapshotJson);
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
        Assert.Empty(w.Notifications(task, TaskEvidenceWork.ReassignmentRequired));
        var replacementOffer = Assert.Single(w.Store.Values.OfType<TaskOffer>(), o => o.TaskId == task.TaskId && o.Status == TaskOfferStatus.Pending);
        Assert.Equal((b, 2, TaskDispatchScope.AdjacentSameFloor), (replacementOffer.StaffUserId, replacementOffer.WaveNumber, replacementOffer.DispatchScope));
        Assert.Empty(await w.StaffTasks(a).Offers(true, default));
        Assert.DoesNotContain(await w.Operator(op).AssignableStaff(task.TaskId, default), s => s.UserId == a);
        Assert.Equal("STAFF_REJECTED_TWICE", (await Assert.ThrowsAsync<AppError>(() => w.Operator(op).Assign(task.TaskId, new(a, "Retry"), default))).Code);

        var reassigned = (await w.StaffTasks(b).Respond(replacementOffer.OfferId, new(TaskResponse.HandleNow, null, null), default)).Task!;
        Assert.Equal((WorkTaskStatus.InProgress, b), (reassigned.Status, reassigned.AssigneeUserId));
        // The former performer still sees their own submissions, the new one starts clean.
        Assert.Equal(2, (await w.Evidence(a).Get(task.TaskId, default)).Submissions.Length);
        Assert.Empty((await w.Evidence(b).Get(task.TaskId, default)).Submissions);

        w.Clock.UtcNow = Now.AddMinutes(10);
        await w.Evidence(b).Submit(task.TaskId, new("Replacement completed"), default);
        var terminal = await w.Evidence(op).Reject(task.TaskId, new("Replacement still failed"), default);
        Assert.Equal((WorkTaskStatus.Offered, 3, (Guid?)null), (terminal.Status, terminal.RejectionCount, terminal.AssigneeUserId));
        Assert.Equal([op], w.Recipients(Assert.Single(w.Notifications(task, TaskEvidenceWork.ReassignmentRequired))));
        Assert.True((await w.Operator(op).Get(task.TaskId, default)).NeedsAssignment);
        Assert.DoesNotContain(w.Store.Values.OfType<TaskOffer>(), o => o.TaskId == task.TaskId && o.Status == TaskOfferStatus.Pending);
        Assert.DoesNotContain(await w.Operator(op).AssignableStaff(task.TaskId, default), s => s.UserId == b);
        Assert.Equal("STAFF_REJECTED_TWICE", (await Assert.ThrowsAsync<AppError>(() => w.Operator(op).Assign(task.TaskId, new(b, "Retry"), default))).Code);
        Assert.Equal(deadlines, (task.ResponseDeadlineAt, task.ResolutionDeadlineAt, task.SlaSnapshotJson));
        Assert.Single(w.Store.Values.OfType<WorkTask>(), t => t.TaskId == task.TaskId);
        Assert.Equal(1, await w.Dispatch.Tick(default));
        Assert.Single(w.Notifications(task, TaskNotifier.NeedsAssignment));
        Assert.Equal(0, await w.Dispatch.Tick(default));
    }

    [Fact]
    public async Task VerifyingTheLastMandatoryTaskClosesItsIncident()
    {
        var w = new World(); var a = w.Staff("An"); var op = w.User("OPERATOR", "Olivia");
        var task = await w.InProgress(a, incidentRequiresBefore: false);
        task.IsMandatory = true;
        var incident = w.Store.Values.OfType<Incident>().Single(i => i.IncidentId == task.IncidentId);
        await w.Upload(a, task, TaskEvidenceType.AfterPhoto);
        await w.Evidence(a).Submit(task.TaskId, new("Mopped and dried"), default);

        await w.Evidence(op).Verify(task.TaskId, new(), default);

        Assert.Equal((IncidentClosure.ClosedStatus, IncidentClosure.AllMandatoryVerified, (DateTime?)Now), (incident.Status, incident.StatusReason, incident.ClosedAt));
        Assert.Null(incident.MitigatedAt);
        var audit = Assert.Single(w.Store.Values.OfType<AuditLog>());
        Assert.Equal(("CLOSE_INCIDENT", (Guid?)incident.IncidentId, (Guid?)op), (audit.Action, audit.EntityId, audit.UserId));
        Assert.Equal([op], w.Recipients(Assert.Single(w.Notifications(task, IncidentClosure.Closed))));
    }

    [Fact]
    public async Task AnIncidentStaysOpenWhileAnotherMandatoryTaskIsNotVerifiedOrTheVerifiedTaskIsOptional()
    {
        var w = new World(); var a = w.Staff("An"); var op = w.User("OPERATOR", "Olivia");
        var task = await w.InProgress(a, incidentRequiresBefore: false);
        task.IsMandatory = true;
        var incident = w.Store.Values.OfType<Incident>().Single(i => i.IncidentId == task.IncidentId);
        var other = new WorkTask { TaskId = Guid.NewGuid(), IncidentId = incident.IncidentId, ZoneId = task.ZoneId, Title = "Fix the leaking pipe", Priority = "WARNING",
            Status = WorkTaskStatus.Offered, OriginType = TaskOrigin.Incident, IsMandatory = true, CreatedAt = Now.AddMinutes(-11) };
        w.Store.Values.Add(other);
        await w.Upload(a, task, TaskEvidenceType.AfterPhoto);
        await w.Evidence(a).Submit(task.TaskId, new("Mopped"), default);
        await w.Evidence(op).Verify(task.TaskId, new(), default);
        Assert.Equal(("DETECTED", (DateTime?)null), (incident.Status, incident.ClosedAt));

        // An optional Task never closes the Incident on its own, even when every mandatory Task is done.
        other.Status = WorkTaskStatus.Verified;
        var optional = await w.InProgress(a, incidentRequiresBefore: false);
        optional.IncidentId = incident.IncidentId;
        await w.Upload(a, optional, TaskEvidenceType.AfterPhoto);
        await w.Evidence(a).Submit(optional.TaskId, new("Put up a sign"), default);
        await w.Evidence(op).Verify(optional.TaskId, new(), default);
        Assert.Equal(("DETECTED", (DateTime?)null), (incident.Status, incident.ClosedAt));
        Assert.Empty(w.Store.Values.OfType<AuditLog>());
    }

    [Theory]
    [InlineData("TT-12", true)]
    [InlineData("TT-13", false)]
    [InlineData("TT-06", false)]
    public async Task OnlyManualVerificationOfTt12RecordsIncidentMitigation(string code, bool mitigates)
    {
        var w = new World(); var staff = w.Staff("An"); var op = w.User("OPERATOR", "Olivia");
        var task = await w.InProgress(staff, incidentRequiresBefore: false);
        var type = w.Store.Values.OfType<TaskType>().Single(t => t.TaskTypeId == task.TaskTypeId);
        type.Code = code; type.Name = "Unrelated display name";
        var incident = w.Store.Values.OfType<Incident>().Single(i => i.IncidentId == task.IncidentId);
        await w.Upload(staff, task, TaskEvidenceType.AfterPhoto);
        await w.Evidence(staff).Submit(task.TaskId, new("Area secured"), default);
        Assert.Null(incident.MitigatedAt);

        w.Clock.UtcNow = Now.AddMinutes(2);
        await w.Evidence(op).Verify(task.TaskId, new(), default);

        Assert.Equal(mitigates ? w.Clock.UtcNow : (DateTime?)null, incident.MitigatedAt);
        Assert.Null(incident.ClosedAt);
        var audits = w.Store.Values.OfType<AuditLog>().Where(a => a.Action == "MITIGATE_INCIDENT").ToList();
        Assert.Equal(mitigates ? 1 : 0, audits.Count);
        if (mitigates) Assert.Equal((incident.IncidentId, op), (audits.Single().EntityId!.Value, audits.Single().UserId!.Value));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tt12AndTt13KeepMitigationAndClosureIndependentInEitherVerificationOrder(bool isolationFirst)
    {
        var w = new World(); var staff = w.Staff("An"); var op = w.User("OPERATOR", "Olivia");
        var isolation = await w.InProgress(staff, incidentRequiresBefore: false);
        w.Store.Values.OfType<TaskType>().Single(t => t.TaskTypeId == isolation.TaskTypeId).Code = "TT-12";
        isolation.IsMandatory = true;
        var incident = w.Store.Values.OfType<Incident>().Single(i => i.IncidentId == isolation.IncidentId);
        var correctionType = new TaskType { TaskTypeId = Guid.NewGuid(), Code = "TT-13", Name = "Resolve Electrical Hazard" };
        var correction = new WorkTask { TaskId = Guid.NewGuid(), TaskTypeId = correctionType.TaskTypeId,
            IncidentId = incident.IncidentId, ZoneId = isolation.ZoneId, OriginType = TaskOrigin.Incident,
            Status = WorkTaskStatus.InProgress, IsMandatory = true, CurrentAssigneeUserId = staff,
            VerificationMode = TaskVerificationMode.Manual, CreatedAt = Now };
        w.Store.Values.AddRange([correctionType, correction]);
        var first = isolationFirst ? isolation : correction;
        var second = isolationFirst ? correction : isolation;
        await w.Upload(staff, first, TaskEvidenceType.AfterPhoto);
        await w.Evidence(staff).Submit(first.TaskId, new("Work completed"), default);
        await w.Evidence(op).Verify(first.TaskId, new(), default);
        Assert.Equal(isolationFirst ? Now : (DateTime?)null, incident.MitigatedAt);
        Assert.Null(incident.ClosedAt);

        w.Clock.UtcNow = Now.AddMinutes(5);
        await w.Upload(staff, second, TaskEvidenceType.AfterPhoto);
        await w.Evidence(staff).Submit(second.TaskId, new("Work completed"), default);
        await w.Evidence(op).Verify(second.TaskId, new(), default);
        Assert.Equal(isolationFirst ? Now : w.Clock.UtcNow, incident.MitigatedAt);
        Assert.Equal((IncidentClosure.ClosedStatus, (DateTime?)w.Clock.UtcNow), (incident.Status, incident.ClosedAt));
    }

    [Fact]
    public async Task VerifyingAnotherTt12PreservesTheFirstMitigationTimestamp()
    {
        var w = new World(); var staff = w.Staff("An"); var op = w.User("OPERATOR", "Olivia");
        var task = await w.InProgress(staff, incidentRequiresBefore: false);
        w.Store.Values.OfType<TaskType>().Single(t => t.TaskTypeId == task.TaskTypeId).Code = "TT-12";
        var incident = w.Store.Values.OfType<Incident>().Single(i => i.IncidentId == task.IncidentId);
        incident.MitigatedAt = Now.AddMinutes(-1);
        await w.Upload(staff, task, TaskEvidenceType.AfterPhoto);
        await w.Evidence(staff).Submit(task.TaskId, new("Isolation checked"), default);
        await w.Evidence(op).Verify(task.TaskId, new(), default);
        Assert.Equal(Now.AddMinutes(-1), incident.MitigatedAt);
        Assert.DoesNotContain(w.Store.Values.OfType<AuditLog>(), a => a.Action == "MITIGATE_INCIDENT");
    }

    private sealed class World
    {
        public MonitoringSetupTests.MemoryStore Store { get; } = new();
        public StaffTaskTests.Clock Clock { get; } = new() { UtcNow = Now };
        public MemoryFiles Files { get; } = new();
        public TaskDispatch Dispatch { get; }
        private readonly StoreCalendar calendar = new(StoreCalendar.Zone(null));
        private readonly Zone zone;
        private readonly Zone adjacentZone;
        private readonly Schema schema = new();
        private readonly StaffRoster roster;
        private readonly TaskNotifier notifier;
        private readonly TaskVerification verification;
        private readonly TaskViews views;
        private readonly Guid shiftId = Guid.NewGuid(), specialtyId = Guid.NewGuid(), taskTypeId = Guid.NewGuid();

        public World()
        {
            var market = new Supermarket.Domain.Supermarket { SupermarketId = Guid.NewGuid(), Code = "SM-01", Name = "Test market" };
            var floor = new Floor { FloorId = Guid.NewGuid(), SupermarketId = market.SupermarketId, FloorNumber = 1, Name = "Floor 1" };
            zone = new Zone { ZoneId = Guid.NewGuid(), FloorId = floor.FloorId, Code = "B", Name = "Zone B · Checkout" };
            adjacentZone = new Zone { ZoneId = Guid.NewGuid(), FloorId = floor.FloorId, Code = "C", Name = "Zone C · Produce" };
            foreach (var role in new[] { "ADMIN", "OPERATOR", "MANAGER", "STAFF" }) Store.Values.Add(new Role { RoleId = Guid.NewGuid(), Name = role });
            Store.Values.AddRange([market, floor, zone, adjacentZone,
                new ZoneAdjacency { ZoneAdjacencyId = Guid.NewGuid(), ZoneId = zone.ZoneId, AdjacentZoneId = adjacentZone.ZoneId, CreatedAt = Now },
                new StaffSpecialty { SpecialtyId = specialtyId, Code = "SPC-01", Name = "Cashier" },
                new TaskType { TaskTypeId = taskTypeId, Code = "TT-01", Name = "Open checkout", RequiredSpecialtyId = specialtyId },
                new Shift { ShiftId = shiftId, SupermarketId = market.SupermarketId, Name = "Morning", StartTime = new(8, 0), EndTime = new(16, 0) }]);
            roster = new StaffRoster(Store, calendar);
            notifier = new TaskNotifier(Store, Clock);
            verification = new TaskVerification(Store, Clock, notifier);
            Dispatch = new TaskDispatch(Store, roster, notifier, verification, Clock, schema);
            views = new TaskViews(Store);
        }

        public Guid User(string role, string name)
        {
            var user = new UserAccount { UserId = Guid.NewGuid(), RoleId = Store.Values.OfType<Role>().Single(r => r.Name == role).RoleId, FullName = name, Email = $"{name}@test", Status = "ACTIVE",
                SpecialtyId = role == "STAFF" ? specialtyId : null };
            Store.Values.Add(user);
            return user.UserId;
        }

        public Guid Staff(string name, bool adjacent = false)
        {
            var id = User("STAFF", name);
            var assignment = new ShiftAssignment { ShiftAssignmentId = Guid.NewGuid(), UserId = id, ShiftId = shiftId, WorkDate = new(2026, 10, 11), CheckInAt = Now.AddHours(-2) };
            Store.Values.Add(assignment);
            Store.Values.Add(new StaffZoneAssignment { StaffZoneAssignmentId = Guid.NewGuid(), ShiftAssignmentId = assignment.ShiftAssignmentId,
                ZoneId = adjacent ? adjacentZone.ZoneId : zone.ZoneId, AssignedAt = Now.AddHours(-3) });
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
                TaskTypeId = taskTypeId, CurrentAssigneeUserId = staff, AssignedAt = Now.AddMinutes(-10), AcceptedAt = Now.AddMinutes(-10), CreatedAt = Now.AddMinutes(-11),
                ResponseDeadlineAt = Now.AddMinutes(5), ResolutionDeadlineAt = Now.AddHours(1), SlaSnapshotJson = "{\"offerTimeoutSec\":60}",
                VerificationMode = TaskVerificationMode.Manual, VerificationMethod = TaskVerificationMethod.OperatorEvidence };
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

        public OperationalEvent AutoEvent(WorkTask task, bool cleared, bool validMetric)
        {
            task.VerificationMode = TaskVerificationMode.Auto; task.VerificationMethod = TaskVerificationMethod.OperationalMetric;
            task.EventId = Guid.NewGuid();
            var ev = new OperationalEvent { EventId = task.EventId.Value, CameraId = Guid.NewGuid(), ZoneId = task.ZoneId,
                MetricValue = cleared ? 1 : 5, Status = cleared ? "CLEARED" : "ACTIVE", DetectedAt = Now.AddMinutes(-5),
                LastObservedAt = Now, ClearedAt = cleared ? Now : null, MetadataJson = MetricJson(cleared ? 1 : 5, validMetric) };
            Store.Values.Add(ev);
            return ev;
        }

        public static string MetricJson(decimal metric, bool valid) => valid
            ? $$"""{"measurementMode":"QUEUE_LENGTH","thresholdUnit":"PEOPLE","metric":{{metric}},"warningThreshold":3}"""
            : "{\"measurementMode\":\"QUEUE_LENGTH\",\"metric\":999}";

        public TaskEvidenceWork Evidence(Guid user) => new(Store, Current(user), Clock, notifier, Dispatch, verification, schema, Files);
        public StaffTasks StaffTasks(Guid user) => new(Store, Current(user), Clock, calendar, roster, Dispatch, views, schema);
        public OperatorTasks Operator(Guid user) => new(Store, Current(user), Clock, roster, Dispatch, notifier, views, schema,
            new StaffTaskTests.OperatorQueries(Store), new StandaloneTaskSlaPolicyService(Store, Clock));

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
