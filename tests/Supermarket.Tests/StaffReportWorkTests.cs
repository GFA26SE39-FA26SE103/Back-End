using Supermarket.Application;
using Supermarket.Domain;
using Xunit;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Tests;

public sealed class StaffReportWorkTests
{
    private static readonly Guid ZoneId = Guid.Parse("2bc6e3f6-dd30-484c-b108-ffdc8c18c012");
    private static readonly Guid TypeId = Guid.Parse("2bc6e3f6-dd30-484c-b108-ffdc8c18c013");

    [Fact]
    public async Task ConfirmedIncidentTypeCodeResolvesToForeignKeyAndStoresPhotoIdentifier()
    {
        var (work, store) = World("ACTIVE");
        store.Values.Add(Policy());
        var view = await work.Submit(Input("SAFETY_HAZARD"),
            [new StaffReportPhoto(Stream.Null, "hazard.jpg", "image/jpeg", 5)], default);
        Assert.Equal(TypeId, view.IncidentTypeId);
        Assert.Equal("POTENTIAL_INCIDENT", view.ReportCategory);
        Assert.Equal(["TT-12"], view.ConfirmedTaskTypeCodes);
        Assert.Equal(TypeId, Assert.Single(store.Values.OfType<StaffReport>()).IncidentTypeId);
        Assert.Equal(view.ReportId, Assert.Single(store.Values.OfType<Incident>()).StaffReportId);
        Assert.NotNull(view.IncidentId);
        var task = Assert.Single(store.Values.OfType<WorkTask>());
        Assert.Equal(view.IncidentId, task.IncidentId);
        Assert.Equal(TaskOrigin.Incident, task.OriginType);
        Assert.True(task.IsMandatory);
        Assert.NotNull(task.SlaSnapshotJson);
        Assert.StartsWith("cld1:", Assert.Single(store.Values.OfType<StaffReportMedium>()).StorageKey);
    }

    [Fact]
    public async Task AVoiceNoteIsStoredAsAudioMediaApartFromThePhotos()
    {
        var (_, store) = World("ACTIVE");
        store.Values.Add(Policy());
        var voice = new Voice();
        var work = CreateWork(store, new User("STAFF"), new Clock(), voice);
        var view = await work.Submit(Input("SAFETY_HAZARD"), [new StaffReportPhoto(Stream.Null, "hazard.jpg", "image/jpeg", 5)],
            new StaffReportAudio(Stream.Null, "note.m4a", "audio/mp4", 2048), default);

        var photo = Assert.Single(view.Photos);
        Assert.Equal("image/jpeg", photo.ContentType);
        Assert.NotNull(view.VoiceNote);
        Assert.Equal(("audio/mp4", 2048L), (view.VoiceNote!.ContentType, view.VoiceNote.SizeBytes));
        var audio = Assert.Single(store.Values.OfType<StaffReportMedium>(), m => m.MediaType == StaffReportWork.AudioMedia);
        Assert.Equal("cla1:test-voice", audio.StorageKey);
        Assert.Equal("audio/mp4", (await work.Image(view.ReportId, audio.MediaId, default)).ContentType);
        Assert.Equal(view.VoiceNote.MediaId, (await work.Get(view.ReportId, default)).VoiceNote!.MediaId);
    }

    [Fact]
    public async Task AnOversizedVoiceNoteOrAServerWithoutAudioStorageSavesNothing()
    {
        var (withoutAudio, store) = World("ACTIVE");
        store.Values.Add(Policy());
        var note = new StaffReportAudio(Stream.Null, "note.m4a", "audio/mp4", 2048);
        Assert.Equal("REPORT_VOICE_STORAGE_NOT_CONFIGURED",
            (await Assert.ThrowsAsync<AppError>(() => withoutAudio.Submit(Input("SAFETY_HAZARD"), [], note, default))).Code);
        var work = CreateWork(store, new User("STAFF"), new Clock(), new Voice());
        Assert.Equal("REPORT_VOICE_SIZE", (await Assert.ThrowsAsync<AppError>(() => work.Submit(Input("SAFETY_HAZARD"), [],
            note with { Length = StaffReportWork.MaxVoiceBytes + 1 }, default))).Code);
        Assert.Empty(store.Values.OfType<StaffReport>());
        Assert.Empty(store.Values.OfType<StaffReportMedium>());
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0x20, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'M', (byte)'4', (byte)'A', (byte)' ' }, ".m4a")]
    [InlineData(new byte[] { (byte)'I', (byte)'D', (byte)'3', 4, 0, 0 }, ".mp3")]
    [InlineData(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'A', (byte)'V', (byte)'E' }, ".wav")]
    [InlineData(new byte[] { (byte)'O', (byte)'g', (byte)'g', (byte)'S' }, ".ogg")]
    [InlineData(new byte[] { 0xFF, 0xF1, 0x50, 0x80 }, ".aac")]
    public void VoiceNoteFormatsAreRecognisedByTheirBytes(byte[] header, string extension)
        => Assert.Equal(extension, Supermarket.Infrastructure.Tasks.AudioContent.Detect(header)?.Extension);

    [Fact]
    public async Task AVoiceNoteThatIsNotAudioIsRejected()
    {
        var bytes = new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4]);
        var error = await Assert.ThrowsAsync<AppError>(() => Supermarket.Infrastructure.Tasks.AudioContent.Read(bytes, bytes.Length, StaffReportWork.MaxVoiceBytes, default));
        Assert.Equal("REPORT_VOICE_FORMAT", error.Code);
    }

    [Fact]
    public async Task OrdinaryTaskReportHasNoIncidentType()
    {
        var (work, store) = World("ACTIVE");
        store.Values.Add(RoutinePolicy(store));
        var view = await work.Submit(Input(null), [], default);
        Assert.Null(view.IncidentTypeId);
        Assert.Null(Assert.Single(store.Values.OfType<StaffReport>()).IncidentTypeId);
        Assert.Equal("ROUTINE_REQUEST", view.ReportCategory);
        Assert.Equal("ROUTINE_REQUEST", Assert.Single(store.Values.OfType<StaffReport>()).ReportCategory);
        var task = Assert.Single(store.Values.OfType<WorkTask>());
        Assert.Equal(TaskOrigin.StaffReport, task.OriginType);
        Assert.Equal(view.ReportId, task.StaffReportId);
        Assert.NotNull(task.SlaSnapshotJson);
        Assert.Equal(new DateTime(2026, 11, 9, 9, 10, 0, DateTimeKind.Utc), task.ResponseDeadlineAt);
        Assert.Equal(new DateTime(2026, 11, 9, 11, 0, 0, DateTimeKind.Utc), task.ResolutionDeadlineAt);
    }

    [Fact]
    public async Task RoutineReportWithoutPolicyDoesNotPersist()
    {
        var (work, store) = World("ACTIVE");
        var error = await Assert.ThrowsAsync<AppError>(() => work.Submit(Input(null), [], default));
        Assert.Equal("TASK_SLA_POLICY_MISSING", error.Code);
        Assert.Empty(store.Values.OfType<StaffReport>());
        Assert.Empty(store.Values.OfType<WorkTask>());
    }

    [Fact]
    public async Task AmbiguousRoutinePolicyDoesNotPersistReport()
    {
        var (work, store) = World("ACTIVE");
        store.Values.Add(RoutinePolicy(store));
        store.Values.Add(RoutinePolicy(store));
        var error = await Assert.ThrowsAsync<AppError>(() => work.Submit(Input(null), [], default));
        Assert.Equal("TASK_SLA_POLICY_AMBIGUOUS", error.Code);
        Assert.Empty(store.Values.OfType<StaffReport>());
    }

    [Fact]
    public async Task RoutineOverlapRequiresStaffChoiceAndDoesNotAutoMerge()
    {
        var (work, store) = World("ACTIVE");
        store.Values.Add(RoutinePolicy(store));
        var typeId = store.Values.OfType<TaskType>().Single(type => type.Code == "TT-11").TaskTypeId;
        store.Values.Add(new WorkTask { TaskId = Guid.NewGuid(), ZoneId = ZoneId, TaskTypeId = typeId,
            OriginType = TaskOrigin.OperationalEvent, Status = WorkTaskStatus.Offered });
        Assert.Single(await work.RoutineOverlaps(ZoneId, "TT-11", default));
        var error = await Assert.ThrowsAsync<AppError>(() => work.Submit(Input(null), [], default));
        Assert.Equal("ROUTINE_DECISION_REQUIRED", error.Code);
        var covered = await work.Submit(Input(null) with { RoutineTaskDecision = "ALREADY_COVERED" }, [], default);
        Assert.Null(covered.IncidentId);
        Assert.Single(store.Values.OfType<WorkTask>());
        await work.Submit(Input(null) with { RoutineTaskDecision = "SEPARATE_TASK" }, [], default);
        Assert.Equal(2, store.Values.OfType<WorkTask>().Count());
    }

    [Theory]
    [InlineData(null, "REPORT_CATEGORY_INVALID")]
    [InlineData("OTHER", "REPORT_CATEGORY_INVALID")]
    [InlineData("ROUTINE_REQUEST", "REPORT_CATEGORY_MISMATCH")]
    public async Task InvalidOrMismatchedCategoryIsRejected(string? category, string expected)
    {
        var (work, store) = World("ACTIVE");
        var error = await Assert.ThrowsAsync<AppError>(() => work.Submit(Input("SAFETY_HAZARD") with { ReportCategory = category }, [], default));
        Assert.Equal(expected, error.Code);
        Assert.Empty(store.Values.OfType<StaffReport>());
    }

    [Fact]
    public async Task PotentialIncidentRequiresConfirmedIncidentType()
    {
        var (work, store) = World("ACTIVE");
        var error = await Assert.ThrowsAsync<AppError>(() => work.Submit(Input(null) with { ReportCategory = "POTENTIAL_INCIDENT" }, [], default));
        Assert.Equal("INCIDENT_TYPE_REQUIRED", error.Code);
        Assert.Empty(store.Values.OfType<StaffReport>());
    }

    [Fact]
    public async Task IncidentReportRequiresStaffConfirmedTaskType()
    {
        var (work, store) = World("ACTIVE");
        var error = await Assert.ThrowsAsync<AppError>(() => work.Submit(Input("SAFETY_HAZARD") with { TaskTypeCodes = [] }, [], default));
        Assert.Equal("TASK_TYPE_REQUIRED", error.Code);
        Assert.Empty(store.Values.OfType<Incident>());
    }

    [Fact]
    public async Task CorrectiveTaskCatalogExposesIncidentTaskTypes()
    {
        var (work, _) = World("ACTIVE");
        var taskTypes = await work.TaskTypes(default);
        Assert.Equal(["TT-11", "TT-12"], taskTypes.Select(type => type.Code).ToArray());
        Assert.True(taskTypes.Single(type => type.Code == "TT-11").RoutineEligible);
        Assert.False(taskTypes.Single(type => type.Code == "TT-12").RoutineEligible);
        Assert.Equal(["TT-12", "TT-13", "TT-14", "TT-15", "TT-16"],
            Assert.Single(await work.IncidentTypes(default)).AllowedTaskTypeCodes);
    }

    [Fact]
    public async Task ElectricalRepairRequiresIsolationTask()
    {
        var (work, store) = World("ACTIVE");
        store.Values.Add(new TaskType { TaskTypeId = Guid.NewGuid(), Code = "TT-13", Name = "Resolve Electrical Hazard",
            SourceCategory = "Safety Hazards", RequiredSpecialtyId = Guid.NewGuid() });
        var error = await Assert.ThrowsAsync<AppError>(() => work.Submit(Input("SAFETY_HAZARD") with { TaskTypeCodes = ["TT-13"] }, [], default));
        Assert.Equal("TASK_TYPE_PAIR_REQUIRED", error.Code);
        Assert.Empty(store.Values.OfType<Incident>());
    }

    [Fact]
    public async Task ConfirmedExposedWireRequiresBothMandatoryTasks()
    {
        var (work, store) = World("ACTIVE");
        var error = await Assert.ThrowsAsync<AppError>(() => work.Submit(Input("SAFETY_HAZARD") with
            { ConfirmedScenarioCode = "EXPOSED_ELECTRICAL_WIRE" }, [], default));
        Assert.Equal("SCENARIO_TASK_TYPES_INVALID", error.Code);
        Assert.Empty(store.Values.OfType<StaffReport>());
    }

    [Fact]
    public async Task StaffConfirmedExposedWirePersistsPairWithoutAiInference()
    {
        var (work, store) = World("ACTIVE");
        store.Values.Add(Policy());
        store.Values.Add(new TaskType { TaskTypeId = Guid.NewGuid(), Code = "TT-13", Name = "Resolve Electrical Hazard",
            SourceCategory = "Safety Hazards", RequiredSpecialtyId = Guid.NewGuid() });
        var view = await work.Submit(Input("SAFETY_HAZARD") with
            { ConfirmedScenarioCode = "EXPOSED_ELECTRICAL_WIRE", TaskTypeCodes = ["TT-12", "TT-13"] }, [], default);
        Assert.Equal("EXPOSED_ELECTRICAL_WIRE", view.ConfirmedScenarioCode);
        Assert.Equal(2, store.Values.OfType<WorkTask>().Count());
    }

    [Fact]
    public async Task ConfirmedScenarioRejectsDifferentIncidentType()
    {
        var (work, store) = World("ACTIVE");
        var error = await Assert.ThrowsAsync<AppError>(() => work.Submit(Input(null) with
            { ConfirmedScenarioCode = "EXPOSED_ELECTRICAL_WIRE" }, [], default));
        Assert.Equal("SCENARIO_INVALID", error.Code);
        Assert.Empty(store.Values.OfType<StaffReport>());
    }

    [Fact]
    public async Task IncidentTypeRejectsUnrelatedCorrectiveTaskType()
    {
        var (work, store) = World("ACTIVE");
        var error = await Assert.ThrowsAsync<AppError>(() => work.Submit(Input("SAFETY_HAZARD") with
            { TaskTypeCodes = ["TT-11"] }, [], default));
        Assert.Equal("TASK_TYPE_INVALID_FOR_INCIDENT", error.Code);
        Assert.Empty(store.Values.OfType<StaffReport>());
    }

    [Fact]
    public async Task RetryWithDifferentCategoryIsRejected()
    {
        var (work, store) = World("ACTIVE");
        store.Values.Add(RoutinePolicy(store));
        var input = Input(null);
        await work.Submit(input, [], default);
        var error = await Assert.ThrowsAsync<AppError>(() => work.Submit(input with { ReportCategory = "POTENTIAL_INCIDENT" }, [], default));
        Assert.Equal("REPORT_ID_CONFLICT", error.Code);
        Assert.Single(store.Values.OfType<StaffReport>());
    }

    [Fact]
    public async Task CategoryIsCanonicalInSavedAndReadReport()
    {
        var (work, store) = World("ACTIVE");
        store.Values.Add(RoutinePolicy(store));
        var input = Input(null) with { ReportCategory = " routine_request " };
        var saved = await work.Submit(input, [], default);
        var loaded = await work.Get(saved.ReportId, default);
        Assert.Equal("ROUTINE_REQUEST", saved.ReportCategory);
        Assert.Equal("ROUTINE_REQUEST", loaded.ReportCategory);
    }

    [Theory]
    [InlineData("INACTIVE", "SAFETY_HAZARD", "INCIDENT_TYPE_INACTIVE")]
    [InlineData("ACTIVE", "MISSING", "INCIDENT_TYPE_UNKNOWN")]
    [InlineData("ACTIVE", " ", "INCIDENT_TYPE_UNKNOWN")]
    public async Task InvalidIncidentTypesFailBeforePersistence(string status, string code, string expected)
    {
        var (work, store) = World(status);
        var error = await Assert.ThrowsAsync<AppError>(() => work.Submit(Input(code), [], default));
        Assert.Equal(expected, error.Code);
        Assert.Empty(store.Values.OfType<StaffReport>());
    }

    [Fact]
    public async Task QualificationUsesTheConfirmedReportForeignKey()
    {
        var (staff, store) = World("ACTIVE");
        store.Values.Add(Policy());
        var report = await staff.Submit(Input("SAFETY_HAZARD"), [], default);
        var operatorWork = OperatorWork(store);
        var result = await operatorWork.QualifyIncident(report.ReportId, default);
        var incident = Assert.Single(store.Values.OfType<Incident>());
        Assert.Equal(TypeId, incident.IncidentTypeId);
        Assert.Equal(report.ReportId, incident.StaffReportId);
        Assert.Equal("STAFF_REPORT", incident.SourceType);
        Assert.Equal(incident.IncidentId, result.IncidentId);
    }

    [Fact]
    public async Task RoutineTaskReportCannotBeQualifiedAsIncident()
    {
        var (staff, store) = World("ACTIVE");
        store.Values.Add(RoutinePolicy(store));
        var report = await staff.Submit(Input(null), [], default);
        var operatorWork = OperatorWork(store);
        var error = await Assert.ThrowsAsync<AppError>(() => operatorWork.QualifyIncident(report.ReportId, default));
        Assert.Equal("INCIDENT_TYPE_REQUIRED", error.Code);
        Assert.Empty(store.Values.OfType<Incident>());
    }

    [Fact]
    public async Task DuplicateWarningShowsOpenIncidentAndRelatedTaskWithoutMerging()
    {
        var (staff, store) = World("ACTIVE");
        var incidentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        store.Values.Add(new Incident { IncidentId = incidentId, ZoneId = ZoneId, IncidentTypeId = TypeId });
        store.Values.Add(new WorkTask { TaskId = taskId, IncidentId = incidentId, ZoneId = ZoneId });
        var matches = await staff.Duplicates(ZoneId, "SAFETY_HAZARD", default);
        Assert.Equal(incidentId, Assert.Single(matches).IncidentId);
        Assert.Equal(taskId, Assert.Single(matches[0].TaskIds));
    }

    [Fact]
    public async Task DistinctIncidentRequiresExplicitStaffChoiceWhenOneIsAlreadyOpen()
    {
        var (staff, store) = World("ACTIVE");
        store.Values.Add(new Incident { IncidentId = Guid.NewGuid(), ZoneId = ZoneId, IncidentTypeId = TypeId });
        store.Values.Add(Policy());
        var missing = await Assert.ThrowsAsync<AppError>(() => staff.Submit(Input("SAFETY_HAZARD"), [], default));
        Assert.Equal("DUPLICATE_DECISION_REQUIRED", missing.Code);
        var report = await staff.Submit(Input("SAFETY_HAZARD") with { DuplicateDecision = "DIFFERENT_INCIDENT" }, [], default);
        var operatorWork = OperatorWork(store);
        await operatorWork.QualifyIncident(report.ReportId, default);
        Assert.Equal(2, store.Values.OfType<Incident>().Count());
    }

    [Fact]
    public async Task AlreadyReportedChoiceDoesNotCreateAnotherIncident()
    {
        var (staff, store) = World("ACTIVE");
        store.Values.Add(new Incident { IncidentId = Guid.NewGuid(), ZoneId = ZoneId, IncidentTypeId = TypeId });
        var report = await staff.Submit(Input("SAFETY_HAZARD") with { DuplicateDecision = "ALREADY_REPORTED" }, [], default);
        Assert.Null(report.IncidentId);
        Assert.Single(store.Values.OfType<Incident>());
        Assert.Empty(store.Values.OfType<WorkTask>());
        Assert.Contains("ALREADY_REPORTED", Assert.Single(store.Values.OfType<StaffReport>()).OriginalInputJson);
    }

    [Fact]
    public async Task ChangingDuplicateDecisionRetainsConfirmedTasksForQualification()
    {
        var (staff, store) = World("ACTIVE");
        store.Values.Add(new Incident { IncidentId = Guid.NewGuid(), ZoneId = ZoneId, IncidentTypeId = TypeId });
        store.Values.Add(Policy());
        var report = await staff.Submit(Input("SAFETY_HAZARD") with { DuplicateDecision = "ALREADY_REPORTED" }, [], default);
        await staff.SetDuplicateDecision(report.ReportId, new("DIFFERENT_INCIDENT"), default);
        await OperatorWork(store).QualifyIncident(report.ReportId, default);
        Assert.Single(store.Values.OfType<WorkTask>());
    }

    [Fact]
    public async Task ExplicitElectricalPairCreatesTwoIncidentTasks()
    {
        var (staff, store) = World("ACTIVE");
        store.Values.Add(new TaskType { TaskTypeId = Guid.NewGuid(), Code = "TT-13", Name = "Resolve Electrical Hazard",
            SourceCategory = "Safety Hazards", RequiredSpecialtyId = Guid.NewGuid() });
        store.Values.Add(Policy());
        var report = await staff.Submit(Input("SAFETY_HAZARD") with { TaskTypeCodes = ["TT-12", "TT-13"] }, [], default);
        var tasks = store.Values.OfType<WorkTask>().ToArray();
        Assert.Equal(2, tasks.Length);
        Assert.All(tasks, task => Assert.Equal(report.IncidentId, task.IncidentId));
        Assert.Equal(2, tasks.Select(task => task.TaskTypeId).Distinct().Count());
    }

    [Fact]
    public async Task RetryingTheSameReportIdReturnsTheExistingIncident()
    {
        var (staff, store) = World("ACTIVE");
        store.Values.Add(Policy());
        var input = Input("SAFETY_HAZARD");
        var first = await staff.Submit(input, [], default);
        var retry = await staff.Submit(input, [], default);
        Assert.Equal(first.ReportId, retry.ReportId);
        Assert.Equal(first.IncidentId, retry.IncidentId);
        Assert.Equal(["TT-12"], retry.ConfirmedTaskTypeCodes);
        Assert.Single(store.Values.OfType<StaffReport>());
        Assert.Single(store.Values.OfType<Incident>());
        Assert.Single(store.Values.OfType<WorkTask>());
    }

    [Fact]
    public async Task MissingSlaPolicyKeepsTheSafetyReportAndAlertsOperator()
    {
        var (staff, store) = World("ACTIVE");
        var roleId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        store.Values.Add(new Role { RoleId = roleId, Name = "OPERATOR" });
        store.Values.Add(new UserAccount { UserId = operatorId, RoleId = roleId, Status = "ACTIVE" });
        var report = await staff.Submit(Input("SAFETY_HAZARD"), [], default);
        Assert.Equal(report.ReportId, Assert.Single(store.Values.OfType<StaffReport>()).ReportId);
        var incident = Assert.Single(store.Values.OfType<Incident>());
        Assert.Equal("INCIDENT_SLA_POLICY_MISSING", incident.StatusReason);
        Assert.Null(incident.SlaPolicyId);
        Assert.Empty(store.Values.OfType<WorkTask>());
        var notice = Assert.Single(store.Values.OfType<Notification>());
        Assert.Equal(incident.IncidentId, notice.IncidentId);
        Assert.Equal(operatorId, Assert.Single(store.Values.OfType<NotificationRecipient>()).UserId);
    }

    private static StaffReportInput Input(string? code) => new(Guid.NewGuid(), ZoneId, "Safety report", "Description", "Near aisle", code)
    { ReportCategory = code is null ? "ROUTINE_REQUEST" : "POTENTIAL_INCIDENT", TaskTypeCodes = code is null ? ["TT-11"] : ["TT-12"] };

    private static SlaPolicy RoutinePolicy(MonitoringSetupTests.MemoryStore store) => new()
    {
        PolicyId = Guid.NewGuid(), ProblemDomain = "STANDALONE_TASK",
        TaskTypeId = store.Values.OfType<TaskType>().Single(type => type.Code == "TT-11").TaskTypeId,
        IsEnabled = true, Version = 1, PriorityCode = "LOW", ResponseTargetSec = 600,
        ResolutionTargetSec = 7200, OfferTimeoutSec = 90, WarningPercent = 80
    };

    private static SlaPolicy Policy() => new() { PolicyId = Guid.NewGuid(), ProblemDomain = "INCIDENT", IncidentTypeId = TypeId,
        IsEnabled = true, Version = 1, PriorityCode = "CRITICAL", ResponseTargetSec = 60,
        ResolutionTargetSec = 900, OfferTimeoutSec = 20, WarningPercent = 80 };

    private static (StaffReportWork Work, MonitoringSetupTests.MemoryStore Store) World(string typeStatus)
    {
        var store = new MonitoringSetupTests.MemoryStore();
        store.Values.Add(new Zone { ZoneId = ZoneId, Status = "ACTIVE" });
        store.Values.Add(new IncidentType { IncidentTypeId = TypeId, Code = "SAFETY_HAZARD", Status = typeStatus });
        store.Values.Add(new TaskType { TaskTypeId = Guid.NewGuid(), Code = "TT-12", Name = "Isolate Hazardous Area", SourceCategory = "Safety Hazards", RequiredSpecialtyId = Guid.NewGuid() });
        store.Values.Add(new TaskType { TaskTypeId = Guid.NewGuid(), Code = "TT-11", Name = "Clean Affected Area", SourceCategory = "Cleanliness Problems", RequiredSpecialtyId = Guid.NewGuid() });
        var clock = new Clock();
        return (CreateWork(store, new User("STAFF"), clock), store);
    }

    private static StaffReportWork OperatorWork(MonitoringSetupTests.MemoryStore store)
    {
        var clock = new Clock();
        return CreateWork(store, new User("OPERATOR"), clock);
    }

    private static StaffReportWork CreateWork(MonitoringSetupTests.MemoryStore store, User current, Clock clock, IStaffReportAudioStorage? audio = null)
    {
        var notifier = new TaskNotifier(store, clock);
        var roster = new StaffRoster(store, new StoreCalendar(StoreCalendar.Zone(null)));
        var dispatch = new TaskDispatch(store, roster, notifier, new TaskVerification(store, clock, notifier), clock, new Schema());
        return new StaffReportWork(store, current, clock, new Media(), new IncidentSlaPolicyService(store, clock),
            new StandaloneTaskSlaPolicyService(store, clock), notifier, dispatch, audio);
    }

    private sealed class Schema : ITaskSchema { public Task<bool> Ready(CancellationToken ct) => Task.FromResult(true); }

    private sealed class User(string role) : ICurrentUser
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public string Role => role;
    }
    private sealed class Clock : IClock { public DateTime UtcNow => new(2026, 11, 9, 9, 0, 0, DateTimeKind.Utc); }
    private sealed class Voice : IStaffReportAudioStorage
    {
        public List<string> Deleted { get; } = [];
        public Task<StoredEvidence> Save(Guid id, Stream content, string name, string type, long length, CancellationToken ct)
            => Task.FromResult(new StoredEvidence("cla1:test-voice", "audio/mp4"));
        public Task<EvidenceFile?> Open(Guid id, string key, CancellationToken ct)
            => Task.FromResult<EvidenceFile?>(key == "cla1:test-voice" ? new EvidenceFile(Stream.Null, "audio/mp4") : null);
        public Task Delete(Guid id, string key, CancellationToken ct) { Deleted.Add(key); return Task.CompletedTask; }
    }

    private sealed class Media : IStaffReportMediaStorage
    {
        public Task<StoredEvidence> Save(Guid id, Stream content, string name, string type, long length, CancellationToken ct)
            => Task.FromResult(new StoredEvidence("cld1:test-asset", type));
        public Task<EvidenceFile?> Open(Guid id, string key, CancellationToken ct)
            => Task.FromResult<EvidenceFile?>(new EvidenceFile(Stream.Null, "image/jpeg"));
        public Task Delete(Guid id, string key, CancellationToken ct) => Task.CompletedTask;
    }
}
