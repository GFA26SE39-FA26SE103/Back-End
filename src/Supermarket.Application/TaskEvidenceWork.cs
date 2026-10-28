using Supermarket.Domain;
namespace Supermarket.Application;

/// <summary>
/// MF-03 Task completion (Report 3 §3.7.4–3.7.5, §3.9.4–3.9.5): the assigned Staff member uploads photos and submits a
/// completion note; an Operator who did not perform the work verifies it or rejects it for rework (BR-25–BR-28).
/// </summary>
/// <remarks>
/// A submission has no own table: it is the COMPLETION_NOTE row, and its photos are the rows uploaded after the
/// previous note. Verifying a Task never resolves its OperationalEvent or closes an Incident (BR-29).
/// </remarks>
public sealed class TaskEvidenceWork(ISetupStore store, ICurrentUser current, IClock clock, TaskNotifier notifier, ITaskSchema schema, ITaskEvidenceStorage files)
{
    public const string Submitted = "TASK_SUBMITTED", Verified = "TASK_VERIFIED", Rejected = "TASK_REJECTED", ReassignmentRequired = "TASK_REASSIGNMENT_REQUIRED";

    public async Task<TaskEvidenceView> Get(Guid taskId, CancellationToken ct)
    {
        await schema.Require(ct);
        var task = await Readable(taskId, ct);
        return await View(task, ct);
    }

    /// <summary>Adds a before/after photo to the draft. A second photo of the same type replaces the first, so a retried upload never duplicates evidence.</summary>
    public async Task<TaskEvidenceView> Upload(Guid taskId, string evidenceType, Stream content, string filename, string contentType, long length, CancellationToken ct)
    {
        UseCase.Staff(current);
        await schema.Require(ct);
        Rules.Status(evidenceType, TaskEvidenceType.Photos);
        Workable(await Assigned(taskId, ct));
        // Store the file first, outside the transaction; an orphan file is removed if the row cannot be written.
        var stored = await files.Save(taskId, content, filename, contentType, length, ct);
        TaskEvidence? replaced = null;
        try
        {
            replaced = await store.Transaction(async () =>
            {
                Workable(await Assigned(taskId, ct));
                var previous = (await Drafts(taskId, ct)).FirstOrDefault(e => e.EvidenceType == evidenceType);
                if (previous is not null) await store.Remove(previous, ct);
                await store.Add(new TaskEvidence { EvidenceId = Guid.NewGuid(), TaskId = taskId, SubmittedByUserId = current.UserId, EvidenceType = evidenceType,
                    EvidenceImage = stored.Token, ReviewStatus = TaskEvidenceReview.Draft, CreatedAt = clock.UtcNow }, ct);
                return previous;
            }, ct);
        }
        catch
        {
            await files.Delete(taskId, stored.Token, ct);
            throw;
        }
        if (replaced?.EvidenceImage is { } old) await files.Delete(taskId, old, ct);
        return await Get(taskId, ct);
    }

    public async Task<TaskEvidenceView> RemoveDraft(Guid taskId, Guid evidenceId, CancellationToken ct)
    {
        UseCase.Staff(current);
        await schema.Require(ct);
        var removed = await store.Transaction(async () =>
        {
            Workable(await Assigned(taskId, ct));
            var draft = (await Drafts(taskId, ct)).SingleOrDefault(e => e.EvidenceId == evidenceId)
                ?? throw new ApplicationException("NOT_FOUND", "The resource was not found.", 404);
            await store.Remove(draft, ct);
            return draft;
        }, ct);
        if (removed.EvidenceImage is { } token) await files.Delete(taskId, token, ct);
        return await Get(taskId, ct);
    }

    public async Task<TaskCompletionResult> Submit(Guid taskId, TaskSubmitRequest request, CancellationToken ct)
    {
        UseCase.Staff(current);
        await schema.Require(ct);
        var note = Rules.Text(request.Note, TaskEvidenceRules.MaxNoteLength, "Completion note");
        await store.Transaction(async () =>
        {
            var task = await Assigned(taskId, ct);
            // Retrying a submission that already went through is not an error.
            if (task.Status == WorkTaskStatus.Submitted) return 0;
            Workable(task);
            Fresh(task, request.ExpectedUpdatedAt);
            var drafts = await Drafts(taskId, ct);
            var requirement = await Requirement(task, ct);
            if (requirement.BeforePhoto && drafts.All(e => e.EvidenceType != TaskEvidenceType.BeforePhoto))
                throw new ApplicationException("EVIDENCE_REQUIRED", "This task needs a before photo.", 422);
            if (requirement.AfterPhoto && drafts.All(e => e.EvidenceType != TaskEvidenceType.AfterPhoto))
                throw new ApplicationException("EVIDENCE_REQUIRED", "This task needs an after photo taken when the work is done.", 422);
            var now = clock.UtcNow;
            foreach (var draft in drafts)
            {
                draft.ReviewStatus = TaskEvidenceReview.Pending;
                await store.Update(draft, ct);
            }
            await store.Add(new TaskEvidence { EvidenceId = Guid.NewGuid(), TaskId = taskId, SubmittedByUserId = current.UserId, EvidenceType = TaskEvidenceType.CompletionNote,
                Note = note, ReviewStatus = TaskEvidenceReview.Pending, CreatedAt = now }, ct);
            task.Status = WorkTaskStatus.Submitted; task.SubmittedAt = now;
            await store.Update(task, ct);
            await notifier.Send(Submitted, "Task waiting for verification", $"{task.Title}: {Name(await store.Find<UserAccount>(current.UserId, ct))} submitted the work.",
                task, await notifier.Operators(ct), "OPERATOR", ct);
            return 0;
        }, ct);
        return await Result(taskId, ct);
    }

    public async Task<TaskCompletionResult> Verify(Guid taskId, TaskVerifyRequest request, CancellationToken ct)
    {
        UseCase.Operator(current);
        await schema.Require(ct);
        await store.Transaction(async () =>
        {
            var (task, submission) = await Reviewable(taskId, request.ExpectedUpdatedAt, ct);
            var requirement = await Requirement(task, ct);
            var photos = (await Photos(task.TaskId, ct)).Where(e => e.ReviewStatus == TaskEvidenceReview.Pending).ToList();
            if ((requirement.BeforePhoto && photos.All(e => e.EvidenceType != TaskEvidenceType.BeforePhoto))
                || (requirement.AfterPhoto && photos.All(e => e.EvidenceType != TaskEvidenceType.AfterPhoto)))
                throw new ApplicationException("EVIDENCE_REQUIRED", "The submission is missing evidence required for this task.", 422);
            var now = clock.UtcNow;
            foreach (var row in photos.Append(submission))
            {
                row.ReviewStatus = TaskEvidenceReview.Verified;
                await store.Update(row, ct);
            }
            task.Status = WorkTaskStatus.Verified; task.VerifiedByUserId = current.UserId; task.VerifiedAt = now;
            await store.Update(task, ct);
            await EndAssignment(task, null, now, ct);
            await notifier.Send(Verified, "Task verified", $"{task.Title}: the Operator confirmed the work.", task, [submission.SubmittedByUserId], "PERFORMER", ct);
            return 0;
        }, ct);
        return await Result(taskId, ct);
    }

    /// <summary>Returns the work for rework; the second rejection of the same Staff member's work sends the Task to the Operator for someone else (BR-28).</summary>
    public async Task<TaskCompletionResult> Reject(Guid taskId, TaskRejectRequest request, CancellationToken ct)
    {
        UseCase.Operator(current);
        await schema.Require(ct);
        var reason = Rules.Text(request.Reason, 2000, "Rejection reason");
        await store.Transaction(async () =>
        {
            var (task, submission) = await Reviewable(taskId, request.ExpectedUpdatedAt, ct);
            var now = clock.UtcNow;
            foreach (var row in (await Photos(task.TaskId, ct)).Where(e => e.ReviewStatus == TaskEvidenceReview.Pending).Append(submission))
            {
                row.ReviewStatus = TaskEvidenceReview.Rejected; row.RejectReason = reason;
                await store.Update(row, ct);
            }
            var performer = submission.SubmittedByUserId;
            var rejectedForPerformer = (await store.List<TaskEvidence>(e => e.TaskId == task.TaskId && e.EvidenceType == TaskEvidenceType.CompletionNote
                && e.SubmittedByUserId == performer && e.ReviewStatus == TaskEvidenceReview.Rejected, ct)).Count;
            task.RejectionCount++; task.SubmittedAt = null;
            if (rejectedForPerformer < TaskEvidenceRules.RejectionsBeforeReassignment)
            {
                task.Status = WorkTaskStatus.InProgress;
                await store.Update(task, ct);
                await notifier.Send(Rejected, "Rework needed", $"{task.Title}: {reason}", task, [performer], "PERFORMER", ct);
                return 0;
            }
            // Second rejection: the Task leaves this Staff member and waits for the Operator to assign someone else.
            task.Status = WorkTaskStatus.Offered; task.CurrentAssigneeUserId = null; task.AssignedByUserId = null; task.AssignedAt = null;
            task.AcceptedAt = null; task.StartedAt = null; task.PlannedStartAt = null;
            await store.Update(task, ct);
            await EndAssignment(task, performer, now, ct);
            await notifier.Send(Rejected, "Task reassigned", $"{task.Title}: rejected twice ({reason}). The Operator will assign it to someone else.", task, [performer], "PERFORMER", ct);
            await notifier.Send(ReassignmentRequired, "Task needs another Staff member", $"{task.Title}: rejected twice. Assign it to a different Staff member.",
                task, await notifier.Operators(ct), "OPERATOR", ct);
            return 0;
        }, ct);
        return await Result(taskId, ct);
    }

    public async Task<EvidenceFile> Image(Guid taskId, Guid evidenceId, CancellationToken ct)
    {
        await schema.Require(ct);
        await Readable(taskId, ct);
        var evidence = await store.Find<TaskEvidence>(evidenceId, ct);
        if (evidence is null || evidence.TaskId != taskId || evidence.EvidenceImage is null)
            throw new ApplicationException("NOT_FOUND", "The resource was not found.", 404);
        // Staff only see their own photos; Operators and Managers see every submission.
        if (current.Role == "STAFF" && evidence.SubmittedByUserId != current.UserId)
            throw new ApplicationException("NOT_FOUND", "The resource was not found.", 404);
        return await files.Open(taskId, evidence.EvidenceImage, ct) ?? throw new ApplicationException("NOT_FOUND", "The resource was not found.", 404);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<WorkTask> Assigned(Guid taskId, CancellationToken ct)
    {
        var task = await store.Find<WorkTask>(taskId, ct);
        if (task is null || task.CurrentAssigneeUserId != current.UserId) throw new ApplicationException("NOT_FOUND", "The resource was not found.", 404);
        return task;
    }

    private static void Workable(WorkTask task)
    {
        if (task.Status != WorkTaskStatus.InProgress)
            throw new ApplicationException("TASK_NOT_IN_PROGRESS", task.Status == WorkTaskStatus.Accepted
                ? "Start the task before adding evidence or submitting it."
                : "Only a task in progress can take evidence or be submitted.");
    }

    private static void Fresh(WorkTask task, DateTime? expected)
    {
        if (expected is { } value && value != task.UpdatedAt)
            throw new ApplicationException("TASK_CHANGED", "This task changed. Reload it before continuing.");
    }

    /// <summary>The assigned Staff member, any Staff member who held the Task, or an operational reader.</summary>
    private async Task<WorkTask> Readable(Guid taskId, CancellationToken ct)
    {
        var task = UseCase.Found(await store.Find<WorkTask>(taskId, ct));
        if (current.Role == "STAFF")
        {
            var me = current.UserId;
            var involved = task.CurrentAssigneeUserId == me || (await store.List<TaskEvidence>(e => e.TaskId == taskId && e.SubmittedByUserId == me, ct)).Count > 0;
            if (!involved) throw new ApplicationException("NOT_FOUND", "The resource was not found.", 404);
        }
        else UseCase.Viewer(current);
        return task;
    }

    private async Task<(WorkTask Task, TaskEvidence Submission)> Reviewable(Guid taskId, DateTime? expected, CancellationToken ct)
    {
        var task = UseCase.Found(await store.Find<WorkTask>(taskId, ct));
        Fresh(task, expected);
        if (task.Status != WorkTaskStatus.Submitted)
            throw new ApplicationException("TASK_NOT_SUBMITTED", "Only a submitted task can be verified or rejected.");
        var submission = (await store.List<TaskEvidence>(e => e.TaskId == taskId && e.EvidenceType == TaskEvidenceType.CompletionNote
            && e.ReviewStatus == TaskEvidenceReview.Pending, ct)).OrderByDescending(e => e.CreatedAt).FirstOrDefault()
            ?? throw new ApplicationException("TASK_NOT_SUBMITTED", "The task has no pending submission.");
        // BR-27: the performer never reviews their own work.
        if (submission.SubmittedByUserId == current.UserId || task.CurrentAssigneeUserId == current.UserId)
            throw new ApplicationException("SELF_VERIFICATION", "The person who performed a task cannot verify it.", 403);
        return (task, submission);
    }

    private async Task EndAssignment(WorkTask task, Guid? performer, DateTime now, CancellationToken ct)
    {
        foreach (var assignment in await store.List<TaskAssignmentHistory>(a => a.TaskId == task.TaskId && a.Status == OfferStatus.Accepted && a.EndedAt == null, ct))
        {
            if (performer is { } staff && assignment.StaffUserId != staff) continue;
            assignment.EndedAt = now;
            if (performer is not null) assignment.Reason = TaskEvidenceRules.RejectedTwiceReason;
            await store.Update(assignment, ct);
        }
    }

    private async Task<EvidenceRequirement> Requirement(WorkTask task, CancellationToken ct)
    {
        IncidentType? type = null;
        if (task.IncidentId is { } incidentId && await store.Find<Incident>(incidentId, ct) is { } incident)
            type = await store.Find<IncidentType>(incident.IncidentTypeId, ct);
        return EvidenceRequirement.For(task, type);
    }

    private Task<List<TaskEvidence>> Drafts(Guid taskId, CancellationToken ct)
        => store.List<TaskEvidence>(e => e.TaskId == taskId && e.ReviewStatus == TaskEvidenceReview.Draft && e.EvidenceType != TaskEvidenceType.CompletionNote, ct);

    private Task<List<TaskEvidence>> Photos(Guid taskId, CancellationToken ct)
        => store.List<TaskEvidence>(e => e.TaskId == taskId && e.EvidenceType != TaskEvidenceType.CompletionNote, ct);

    private static string Name(UserAccount? user) => user?.FullName is { Length: > 0 } name ? name : "The Staff member";

    private async Task<TaskEvidenceView> View(WorkTask task, CancellationToken ct)
    {
        var rows = await store.List<TaskEvidence>(e => e.TaskId == task.TaskId, ct);
        if (current.Role == "STAFF") rows = rows.Where(e => e.SubmittedByUserId == current.UserId).ToList();
        var requirement = await Requirement(task, ct);
        var people = rows.Select(e => e.SubmittedByUserId).Distinct().ToList();
        var names = (await store.List<UserAccount>(u => people.Contains(u.UserId), ct)).ToDictionary(u => u.UserId, u => u.FullName);
        TaskEvidencePhotoView Photo(TaskEvidence e) => new(e.EvidenceId, e.EvidenceType, $"/api/tasks/{task.TaskId}/evidence/{e.EvidenceId}/image", e.CreatedAt);

        var notes = rows.Where(e => e.EvidenceType == TaskEvidenceType.CompletionNote).OrderBy(e => e.CreatedAt).ToList();
        var photos = rows.Where(e => e.EvidenceType != TaskEvidenceType.CompletionNote && e.ReviewStatus != TaskEvidenceReview.Draft).OrderBy(e => e.CreatedAt).ToList();
        var submissions = new List<TaskSubmissionView>();
        DateTime? previous = null;
        foreach (var note in notes)
        {
            // A submission's photos were uploaded after the previous note and before this one.
            var own = photos.Where(p => p.SubmittedByUserId == note.SubmittedByUserId && (previous is null || p.CreatedAt > previous) && p.CreatedAt <= note.CreatedAt);
            submissions.Add(new(note.EvidenceId, note.SubmittedByUserId, names.GetValueOrDefault(note.SubmittedByUserId), note.Note ?? "", note.CreatedAt,
                note.ReviewStatus, note.RejectReason, own.Select(Photo).ToArray()));
            previous = note.CreatedAt;
        }
        var drafts = rows.Where(e => e.ReviewStatus == TaskEvidenceReview.Draft && e.EvidenceType != TaskEvidenceType.CompletionNote).OrderBy(e => e.CreatedAt).Select(Photo).ToArray();
        return new(task.TaskId, task.Status, requirement.BeforePhoto, requirement.AfterPhoto, task.RejectionCount, drafts,
            submissions.OrderByDescending(s => s.SubmittedAt).ToArray());
    }

    private async Task<TaskCompletionResult> Result(Guid taskId, CancellationToken ct)
    {
        var task = UseCase.Found(await store.Find<WorkTask>(taskId, ct));
        return new(task.TaskId, task.Status, task.SubmittedAt, task.VerifiedAt, task.RejectionCount, task.CurrentAssigneeUserId, task.UpdatedAt, await View(task, ct));
    }
}

/// <summary>Task state after a completion action, with its evidence.</summary>
public sealed record TaskCompletionResult(Guid TaskId, string Status, DateTime? SubmittedAt, DateTime? VerifiedAt, int RejectionCount, Guid? AssigneeUserId,
    DateTime UpdatedAt, TaskEvidenceView Evidence);
