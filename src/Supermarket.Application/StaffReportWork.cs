using Supermarket.Domain;
using System.Text.Json;
using System.Text.Json.Nodes;
using Report = Supermarket.Domain.StaffReport;

namespace Supermarket.Application;

public sealed record StaffReportInput(Guid ReportId, Guid ZoneId, string Title, string? Description, string? LocationDescription,
    string? IncidentTypeCode, string? DuplicateDecision = null)
{
    public string? ReportCategory { get; init; }
    public string[]? TaskTypeCodes { get; init; }
    public string? ConfirmedScenarioCode { get; init; }
    public string? RoutineTaskDecision { get; init; }
}
public sealed record StaffReportDuplicateDecisionRequest(string Decision);
public sealed record StaffReportPhoto(Stream Content, string FileName, string ContentType, long Length);
public sealed record StaffReportMediaView(Guid MediaId, string ContentType, long SizeBytes, string ImageUrl);
public sealed record StaffReportView(Guid ReportId, Guid ZoneId, Guid? IncidentTypeId, string? IncidentTypeCode,
    string Title, string? Description, string? LocationDescription, string Status, DateTime? ConfirmedAt,
    StaffReportMediaView[] Photos, Guid? IncidentId)
{
    public string? ReportCategory { get; init; }
    public string[] ConfirmedTaskTypeCodes { get; init; } = [];
    public string? ConfirmedScenarioCode { get; init; }
}
public sealed record StaffReportIncidentView(Guid IncidentId, Guid StaffReportId, Guid IncidentTypeId);
public sealed record StaffReportDuplicateView(Guid IncidentId, Guid[] TaskIds);
public sealed record StaffReportRoutineOverlapView(Guid TaskId, string OriginType, string Status);
public sealed record StaffReportIncidentTypeView(Guid IncidentTypeId, string Code, string Name, string? Description)
{
    public string[] AllowedTaskTypeCodes { get; init; } = [];
}
public sealed record StaffReportTaskTypeView(Guid TaskTypeId, string Code, string Name, string SourceCategory)
{
    public bool RoutineEligible { get; init; }
}
public sealed record StaffReportZoneView(Guid ZoneId, Guid FloorId, string FloorName, string Code, string Name);

/// <summary>Separate capability from Task evidence; implementation reuses the existing authenticated Cloudinary uploader.</summary>
public interface IStaffReportMediaStorage : ITaskEvidenceStorage;

public sealed class StaffReportWork(ISetupStore store, ICurrentUser current, IClock clock,
    IStaffReportMediaStorage media, IncidentSlaPolicyService incidentSla, StandaloneTaskSlaPolicyService taskSla,
    TaskNotifier notifier, TaskDispatch dispatch)
{
    private const string AlreadyReported = "ALREADY_REPORTED";
    private const string DifferentIncident = "DIFFERENT_INCIDENT";
    private const string RoutineRequest = "ROUTINE_REQUEST";
    private const string PotentialIncident = "POTENTIAL_INCIDENT";
    private const string AlreadyCovered = "ALREADY_COVERED";
    private const string SeparateTask = "SEPARATE_TASK";
    private static readonly HashSet<string> RoutineTaskCodes = ["TT-01", "TT-02", "TT-05", "TT-09", "TT-11"];
    private static readonly HashSet<string> IncidentTaskCategories =
        ["Spills", "Equipment Faults", "Pathway Obstruction", "Cleanliness Problems", "Safety Hazards"];
    private static readonly Dictionary<string, string[]> IncidentTaskTypeCodes = new()
    {
        ["SPILL_BROKEN_EQUIPMENT"] = ["TT-06", "TT-07", "TT-08", "TT-09"],
        ["EQUIPMENT_MALFUNCTION"] = ["TT-07", "TT-08", "TT-09"],
        ["PATHWAY_OBSTRUCTION"] = ["TT-10"],
        ["CLEANLINESS_ISSUE"] = ["TT-11"],
        ["SAFETY_HAZARD"] = ["TT-12", "TT-13", "TT-14", "TT-15", "TT-16"]
    };
    public async Task<StaffReportTaskTypeView[]> TaskTypes(CancellationToken ct)
    {
        UseCase.Staff(current);
        var types = (await store.List<TaskType>(ct: ct)).Where(type => IncidentTaskCategories.Contains(type.SourceCategory) || RoutineTaskCodes.Contains(type.Code)).ToArray();
        if (types.Length > 100) throw new ApplicationException("TASK_TYPE_RESULTS_LIMIT", "Too many corrective Task types.", 503);
        return types.OrderBy(type => type.Code)
            .Select(type => new StaffReportTaskTypeView(type.TaskTypeId, type.Code, type.Name, type.SourceCategory)
                { RoutineEligible = RoutineTaskCodes.Contains(type.Code) }).ToArray();
    }
    public async Task<StaffReportIncidentTypeView[]> IncidentTypes(CancellationToken ct)
    {
        UseCase.Staff(current);
        return (await store.List<IncidentType>(x => x.Status == "ACTIVE", ct))
            .Where(x => MonitoringPolicy.OperationalEventCode(x.Code) is null)
            .OrderBy(x => x.Name).Select(x => new StaffReportIncidentTypeView(x.IncidentTypeId, x.Code, x.Name, x.Description)
                { AllowedTaskTypeCodes = IncidentTaskTypeCodes.GetValueOrDefault(x.Code) ?? [] }).ToArray();
    }

    public async Task<StaffReportZoneView[]> Zones(Guid? floorId, CancellationToken ct)
    {
        UseCase.Staff(current);
        var floors = (await store.List<Floor>(ct: ct)).ToDictionary(x => x.FloorId);
        var zones = await store.List<Zone>(x => x.Status == "ACTIVE" && (floorId == null || x.FloorId == floorId), ct);
        if (zones.Count > 500) throw new ApplicationException("ZONE_RESULTS_LIMIT", "Select a Floor to narrow the Zone list.", 422);
        return zones.Where(x => floors.ContainsKey(x.FloorId)).OrderBy(x => floors[x.FloorId].FloorNumber).ThenBy(x => x.Name)
            .Select(x => new StaffReportZoneView(x.ZoneId, x.FloorId, floors[x.FloorId].Name, x.Code, x.Name)).ToArray();
    }
    public async Task<StaffReportDuplicateView[]> Duplicates(Guid zoneId, string incidentTypeCode, CancellationToken ct)
    {
        UseCase.Staff(current);
        var zone = UseCase.Found(await store.Find<Zone>(zoneId, ct));
        if (zone.Status != "ACTIVE") throw new ApplicationException("ZONE_INACTIVE", "The selected Zone is inactive.", 422);
        var type = await ResolveIncidentType(incidentTypeCode, ct)
            ?? throw new ApplicationException("INCIDENT_TYPE_REQUIRED", "Select an Incident type.", 422);
        var incidents = await store.List<Incident>(x => x.ZoneId == zoneId && x.IncidentTypeId == type.IncidentTypeId && x.ClosedAt == null, ct);
        if (incidents.Count > 20) throw new ApplicationException("DUPLICATE_RESULTS_LIMIT", "Too many open Incidents; ask an Operator to review.", 409);
        var result = new List<StaffReportDuplicateView>(incidents.Count);
        foreach (var incident in incidents)
        {
            var tasks = await store.List<WorkTask>(x => x.IncidentId == incident.IncidentId, ct);
            result.Add(new StaffReportDuplicateView(incident.IncidentId, tasks.Take(20).Select(x => x.TaskId).ToArray()));
        }
        return result.ToArray();
    }

    public async Task<StaffReportRoutineOverlapView[]> RoutineOverlaps(Guid zoneId, string taskTypeCode, CancellationToken ct)
    {
        UseCase.Staff(current);
        var zone = UseCase.Found(await store.Find<Zone>(zoneId, ct));
        if (zone.Status != "ACTIVE") throw new ApplicationException("ZONE_INACTIVE", "The selected Zone is inactive.", 422);
        var type = await ResolveRoutineTaskType(taskTypeCode, ct);
        var tasks = await OpenRoutineOverlaps(zoneId, type.TaskTypeId, ct);
        return tasks.Select(task => new StaffReportRoutineOverlapView(task.TaskId, task.OriginType, task.Status)).ToArray();
    }

    public async Task<StaffReportView> Submit(StaffReportInput input, IReadOnlyList<StaffReportPhoto> photos, CancellationToken ct)
    {
        UseCase.Staff(current);
        if (input.ReportId == Guid.Empty) throw new ApplicationException("REPORT_ID_REQUIRED", "Provide a report ID for safe retries.", 422);
        var prior = await store.Find<Report>(input.ReportId, ct);
        if (prior is not null)
        {
            if (prior.ReportedByUserId != current.UserId) throw new ApplicationException("FORBIDDEN", "You cannot use this report ID.", 403);
            var priorCode = prior.IncidentTypeId is Guid priorTypeId ? (await store.Find<IncidentType>(priorTypeId, ct))?.Code : null;
            if (prior.ZoneId != input.ZoneId || prior.Title != input.Title?.Trim()
                || prior.Description != input.Description?.Trim() || prior.LocationDescription != input.LocationDescription?.Trim()
                || prior.ReportCategory != input.ReportCategory?.Trim().ToUpperInvariant()
                || priorCode != input.IncidentTypeCode?.Trim().ToUpperInvariant()
                || !ReadTaskCodes(prior.OriginalInputJson).SequenceEqual(NormalizedTaskCodes(input.TaskTypeCodes))
                || ReadScenarioCode(prior.OriginalInputJson) != input.ConfirmedScenarioCode?.Trim().ToUpperInvariant()
                || ReadRoutineDecision(prior.OriginalInputJson) != input.RoutineTaskDecision?.Trim().ToUpperInvariant()
                || ReadDecision(prior.OriginalInputJson) != input.DuplicateDecision)
                throw new ApplicationException("REPORT_ID_CONFLICT", "This report ID was used for different content.", 409);
            return await Get(input.ReportId, ct);
        }
        if (input.ZoneId == Guid.Empty) throw new ApplicationException("ZONE_REQUIRED", "Select a Zone.", 422);
        var zone = UseCase.Found(await store.Find<Zone>(input.ZoneId, ct));
        if (zone.Status != "ACTIVE") throw new ApplicationException("ZONE_INACTIVE", "The selected Zone is inactive.", 422);
        var title = Rules.Text(input.Title, 200, "Title").Trim();
        Rules.Optional(input.Description, 4000, "Description");
        Rules.Optional(input.LocationDescription, 1000, "Location description");
        var category = ValidateCategory(input.ReportCategory);
        if (category == RoutineRequest && input.IncidentTypeCode is not null)
            throw new ApplicationException("REPORT_CATEGORY_MISMATCH", "Routine requests cannot select an Incident type.", 422);
        if (category == PotentialIncident && input.IncidentTypeCode is null)
            throw new ApplicationException("INCIDENT_TYPE_REQUIRED", "Select an Incident type for this report.", 422);
        var scenario = ValidateScenario(input.ConfirmedScenarioCode, category, input.IncidentTypeCode);
        var incidentType = await ResolveIncidentType(input.IncidentTypeCode, ct);
        var taskTypes = category == PotentialIncident ? await ResolveTaskTypes(input.TaskTypeCodes, incidentType!.Code, ct)
            : [await ResolveRoutineTaskType(AssertSingleRoutineCode(input.TaskTypeCodes), ct)];
        if (scenario == "EXPOSED_ELECTRICAL_WIRE" &&
            !taskTypes.Select(type => type.Code).SequenceEqual(["TT-12", "TT-13"]))
            throw new ApplicationException("SCENARIO_TASK_TYPES_INVALID", "Exposed electrical wire requires exactly TT-12 and TT-13.", 422);
        var decision = ValidateDecision(input.DuplicateDecision);
        var routineDecision = ValidateRoutineDecision(input.RoutineTaskDecision);
        if (incidentType is null && decision is not null)
            throw new ApplicationException("DUPLICATE_DECISION_INVALID", "A routine Task report has no Incident duplicate decision.", 422);
        if (incidentType is not null && routineDecision is not null)
            throw new ApplicationException("ROUTINE_DECISION_INVALID", "Incident reports have no routine Task decision.", 422);
        if (incidentType is null)
            await CheckRoutineOverlap(input.ZoneId, taskTypes[0].TaskTypeId, routineDecision, ct);
        if (incidentType is not null)
        {
            var overlaps = await store.List<Incident>(x => x.ZoneId == input.ZoneId && x.IncidentTypeId == incidentType.IncidentTypeId && x.ClosedAt == null, ct);
            if (overlaps.Count > 0 && decision is null)
                throw new ApplicationException("DUPLICATE_DECISION_REQUIRED", "Review the open Incident and choose Already Reported or Different Incident.", 409);
            if (overlaps.Count == 0 && decision == AlreadyReported)
                throw new ApplicationException("DUPLICATE_DECISION_INVALID", "There is no open matching Incident to mark Already Reported.", 409);
        }
        if (photos.Count > 3) throw new ApplicationException("REPORT_PHOTO_LIMIT", "At most three photos are allowed.", 422);
        if (photos.Any(p => p.Length is <= 0 or > 10L * 1024 * 1024))
            throw new ApplicationException("REPORT_PHOTO_SIZE", "Each photo must be at most 10 MB.", 422);

        WorkTask? routineTask = null;
        if (incidentType is null && routineDecision != AlreadyCovered)
        {
            var type = taskTypes[0];
            routineTask = new WorkTask { TaskId = Guid.NewGuid(), StaffReportId = input.ReportId, ZoneId = input.ZoneId,
                OriginType = TaskOrigin.StaffReport, TaskTypeId = type.TaskTypeId,
                Title = title, Description = input.Description?.Trim(), Status = WorkTaskStatus.Offered };
            await taskSla.Apply(routineTask, ct);
        }

        var id = input.ReportId;
        var uploaded = new List<(StaffReportPhoto Photo, StoredEvidence Asset)>();
        try
        {
            foreach (var photo in photos)
                uploaded.Add((photo, await media.Save(id, photo.Content, photo.FileName, photo.ContentType, photo.Length, ct)));
            var now = clock.UtcNow;
            var report = new Report { ReportId = id, ReportedByUserId = current.UserId, ZoneId = input.ZoneId,
                Title = title, Description = input.Description?.Trim(), LocationDescription = input.LocationDescription?.Trim(),
                OriginalInputJson = JsonSerializer.Serialize(new { duplicateDecision = decision, routineTaskDecision = routineDecision,
                    confirmedTaskTypeCodes = taskTypes.Select(t => t.Code).ToArray(), confirmedScenarioCode = scenario }),
                IncidentTypeId = incidentType?.IncidentTypeId, ReportCategory = category, Status = "CONFIRMED", ConfirmedAt = now,
                ClassifiedAt = incidentType is null ? null : now };
            var rows = uploaded.Select(x => new StaffReportMedium { MediaId = Guid.NewGuid(), ReportId = id,
                UploadedByUserId = current.UserId, MediaType = "IMAGE", StorageKey = x.Asset.Token,
                ContentType = x.Asset.ContentType, SizeBytes = x.Photo.Length, UploadedAt = now }).ToArray();
            Guid? createdIncidentId = null;
            await store.Transaction(async () =>
            {
                if (incidentType is not null)
                {
                    var currentOverlaps = await store.List<Incident>(x => x.ZoneId == input.ZoneId && x.IncidentTypeId == incidentType.IncidentTypeId && x.ClosedAt == null, ct);
                    if (currentOverlaps.Count > 0 && decision is null)
                        throw new ApplicationException("DUPLICATE_DECISION_REQUIRED", "Review the open Incident before submitting.", 409);
                    if (currentOverlaps.Count == 0 && decision == AlreadyReported)
                        throw new ApplicationException("DUPLICATE_DECISION_INVALID", "There is no open matching Incident.", 409);
                }
                else await CheckRoutineOverlap(input.ZoneId, taskTypes[0].TaskTypeId, routineDecision, ct);
                await store.Add(report, ct);
                foreach (var row in rows) await store.Add(row, ct);
                if (routineTask is not null)
                {
                    await store.Add(routineTask, ct);
                    await dispatch.OfferStandaloneTask(routineTask, ct);
                }
                if (incidentType is not null && decision != AlreadyReported)
                {
                    var incident = new Incident { IncidentId = Guid.NewGuid(), StaffReportId = id,
                        ReportedByUserId = current.UserId, ZoneId = input.ZoneId,
                        IncidentTypeId = incidentType.IncidentTypeId, SourceType = "STAFF_REPORT",
                        Title = title, Description = input.Description?.Trim(), Status = "DETECTED" };
                    var policyError = await AssignSlaOrException(incident, ct);
                    await store.Add(incident, ct);
                    if (policyError is not null) await notifier.IncidentPolicyException(incident, policyError, ct);
                    else await CreateCorrectiveTasks(incident, taskTypes, ct);
                    createdIncidentId = incident.IncidentId;
                }
                return true;
            }, ct);
            return View(report, incidentType?.Code, rows, createdIncidentId);
        }
        catch
        {
            foreach (var x in uploaded)
            {
                // Preserve the original transaction/upload error; orphan cleanup is best effort.
                try { await media.Delete(id, x.Asset.Token, CancellationToken.None); }
                catch (Exception e) when (e is IOException or HttpRequestException or OperationCanceledException or ApplicationException) { }
            }
            throw;
        }
    }

    public async Task<StaffReportView> Get(Guid id, CancellationToken ct)
    {
        var report = UseCase.Found(await store.Find<Report>(id, ct));
        CheckReader(report);
        var rows = await store.List<StaffReportMedium>(x => x.ReportId == id, ct);
        var incident = (await store.List<Incident>(x => x.StaffReportId == id, ct)).FirstOrDefault();
        var code = report.IncidentTypeId is Guid typeId ? (await store.Find<IncidentType>(typeId, ct))?.Code : null;
        return View(report, code, rows, incident?.IncidentId);
    }

    public async Task<StaffReportView> SetDuplicateDecision(Guid id, StaffReportDuplicateDecisionRequest request, CancellationToken ct)
    {
        UseCase.Staff(current);
        var decision = ValidateDecision(request.Decision)
            ?? throw new ApplicationException("DUPLICATE_DECISION_INVALID", "Choose a duplicate decision.", 422);
        var report = UseCase.Found(await store.Find<Report>(id, ct));
        if (report.ReportedByUserId != current.UserId) throw new ApplicationException("FORBIDDEN", "You cannot change this report.", 403);
        if (report.IncidentTypeId is not Guid typeId)
            throw new ApplicationException("DUPLICATE_DECISION_INVALID", "Routine Task reports have no Incident duplicate decision.", 422);
        if ((await store.List<Incident>(x => x.StaffReportId == id, ct)).Count > 0)
            throw new ApplicationException("REPORT_ALREADY_QUALIFIED", "The report already has an Incident.", 409);
        var overlaps = await store.List<Incident>(x => x.ZoneId == report.ZoneId && x.IncidentTypeId == typeId && x.ClosedAt == null, ct);
        if (decision == AlreadyReported && overlaps.Count == 0)
            throw new ApplicationException("DUPLICATE_DECISION_INVALID", "There is no open matching Incident.", 409);
        var original = string.IsNullOrWhiteSpace(report.OriginalInputJson) ? new JsonObject()
            : JsonNode.Parse(report.OriginalInputJson) as JsonObject ?? new JsonObject();
        original["duplicateDecision"] = decision;
        report.OriginalInputJson = original.ToJsonString();
        await store.Transaction(async () => { await store.Update(report, ct); return true; }, ct);
        return await Get(id, ct);
    }

    public async Task<EvidenceFile> Image(Guid id, Guid mediaId, CancellationToken ct)
    {
        var report = UseCase.Found(await store.Find<Report>(id, ct));
        CheckReader(report);
        var row = UseCase.Found(await store.Find<StaffReportMedium>(mediaId, ct));
        if (row.ReportId != id) throw new ApplicationException("NOT_FOUND", "Photo not found.", 404);
        return UseCase.Found(await media.Open(id, row.StorageKey, ct));
    }

    public async Task<StaffReportIncidentView> QualifyIncident(Guid reportId, CancellationToken ct)
    {
        UseCase.Operator(current);
        return await store.Transaction(async () =>
        {
            var report = UseCase.Found(await store.Find<Report>(reportId, ct));
            if (report.ConfirmedAt is null) throw new ApplicationException("REPORT_NOT_CONFIRMED", "The Staff Report is not confirmed.", 422);
            if (report.IncidentTypeId is not Guid typeId)
                throw new ApplicationException("INCIDENT_TYPE_REQUIRED", "A routine Task report cannot be qualified as an Incident.", 422);
            var type = UseCase.Found(await store.Find<IncidentType>(typeId, ct));
            if (type.Status != "ACTIVE") throw new ApplicationException("INCIDENT_TYPE_INACTIVE", "The Incident type is inactive.", 422);
            var existing = await store.List<Incident>(x => x.StaffReportId == reportId, ct);
            if (existing.Count > 0) return new StaffReportIncidentView(existing[0].IncidentId, reportId, typeId);
            var choice = ReadDecision(report.OriginalInputJson);
            if (choice == AlreadyReported)
                throw new ApplicationException("REPORT_ALREADY_REPORTED", "Staff marked this as an existing Incident.", 409);
            // Never merge solely by Zone and type. A distinct open Incident requires Staff's explicit choice.
            var overlapping = await store.List<Incident>(x => x.ZoneId == report.ZoneId && x.IncidentTypeId == typeId && x.ClosedAt == null, ct);
            if (overlapping.Count > 0 && choice != DifferentIncident)
                throw new ApplicationException("DUPLICATE_INCIDENT_REVIEW_REQUIRED",
                    "An open Incident with this type and Zone already exists. Staff must choose Different Incident first.", 409);
            var incident = new Incident { IncidentId = Guid.NewGuid(), StaffReportId = reportId,
                ReportedByUserId = report.ReportedByUserId, ZoneId = report.ZoneId, IncidentTypeId = typeId,
                SourceType = "STAFF_REPORT", Title = report.Title, Description = report.Description, Status = "DETECTED" };
            var policyError = await AssignSlaOrException(incident, ct);
            await store.Add(incident, ct);
            if (policyError is not null) await notifier.IncidentPolicyException(incident, policyError, ct);
            else await CreateCorrectiveTasks(incident, await ResolveTaskTypes(ReadTaskCodes(report.OriginalInputJson), type.Code, ct), ct);
            return new StaffReportIncidentView(incident.IncidentId, reportId, typeId);
        }, ct);
    }

    private async Task<IncidentType?> ResolveIncidentType(string? code, CancellationToken ct)
    {
        if (code is null) return null;
        if (string.IsNullOrWhiteSpace(code)) throw new ApplicationException("INCIDENT_TYPE_UNKNOWN", "Unknown Incident type.", 422);
        var normalized = Rules.Text(code, 50, "Incident type code").Trim().ToUpperInvariant();
        var matches = await store.List<IncidentType>(x => x.Code == normalized, ct);
        if (matches.Count == 0) throw new ApplicationException("INCIDENT_TYPE_UNKNOWN", "Unknown Incident type.", 422);
        if (matches.Count != 1) throw new ApplicationException("INCIDENT_TYPE_AMBIGUOUS", "Incident type catalog is ambiguous.", 503);
        if (matches[0].Status != "ACTIVE") throw new ApplicationException("INCIDENT_TYPE_INACTIVE", "The Incident type is inactive.", 422);
        if (MonitoringPolicy.OperationalEventCode(matches[0].Code) is not null)
            throw new ApplicationException("INCIDENT_TYPE_NOT_STAFF_REPORTED", "Choose a Staff-reported Incident type.", 422);
        return matches[0];
    }

    private static string[] NormalizedTaskCodes(string[]? codes)
    {
        if (codes is { Length: > 8 }) throw new ApplicationException("TASK_TYPE_INVALID", "Select at most eight Task types.", 422);
        return (codes ?? []).Select(code => code?.Trim().ToUpperInvariant() ?? "").OrderBy(code => code).ToArray();
    }

    private static string AssertSingleRoutineCode(string[]? supplied)
    {
        var codes = NormalizedTaskCodes(supplied);
        if (codes.Length != 1 || !RoutineTaskCodes.Contains(codes[0]))
            throw new ApplicationException("ROUTINE_TASK_TYPE_INVALID", "Choose one approved routine Task type.", 422);
        return codes[0];
    }

    private async Task<TaskType> ResolveRoutineTaskType(string? code, CancellationToken ct)
    {
        var normalized = AssertSingleRoutineCode(code is null ? null : [code]);
        var matches = await store.List<TaskType>(type => type.Code == normalized, ct);
        if (matches.Count != 1 || matches[0].RequiredSpecialtyId == Guid.Empty)
            throw new ApplicationException("ROUTINE_TASK_TYPE_INVALID", "The routine Task type is not configured.", 422);
        return matches[0];
    }

    private async Task<List<WorkTask>> OpenRoutineOverlaps(Guid zoneId, Guid taskTypeId, CancellationToken ct)
    {
        var tasks = await store.List<WorkTask>(task => task.ZoneId == zoneId && task.TaskTypeId == taskTypeId
            && task.Status != WorkTaskStatus.Verified && task.Status != WorkTaskStatus.Cancelled, ct);
        if (tasks.Count > 20) throw new ApplicationException("ROUTINE_OVERLAP_LIMIT", "Too many matching open Tasks; ask an Operator to review.", 409);
        return tasks;
    }

    private async Task CheckRoutineOverlap(Guid zoneId, Guid taskTypeId, string? decision, CancellationToken ct)
    {
        var overlaps = await OpenRoutineOverlaps(zoneId, taskTypeId, ct);
        if (overlaps.Count > 0 && decision is null)
            throw new ApplicationException("ROUTINE_DECISION_REQUIRED", "A matching Task is open. Choose Already Covered or Separate Task.", 409);
        if (overlaps.Count == 0 && decision == AlreadyCovered)
            throw new ApplicationException("ROUTINE_DECISION_INVALID", "No matching Task is open.", 409);
    }

    private static string? ValidateRoutineDecision(string? decision)
    {
        var normalized = decision?.Trim().ToUpperInvariant();
        if (normalized is not (null or AlreadyCovered or SeparateTask))
            throw new ApplicationException("ROUTINE_DECISION_INVALID", "Use ALREADY_COVERED or SEPARATE_TASK.", 422);
        return normalized;
    }

    private async Task<List<TaskType>> ResolveTaskTypes(string[]? supplied, string incidentTypeCode, CancellationToken ct)
    {
        var codes = NormalizedTaskCodes(supplied);
        if (codes.Length == 0) throw new ApplicationException("TASK_TYPE_REQUIRED", "Confirm at least one corrective Task type.", 422);
        if (codes.Length > 8 || codes.Any(code => code.Length is 0 or > 50) || codes.Distinct().Count() != codes.Length)
            throw new ApplicationException("TASK_TYPE_INVALID", "Select one to eight distinct Task types.", 422);
        if (codes.Contains("TT-13") && !codes.Contains("TT-12"))
            throw new ApplicationException("TASK_TYPE_PAIR_REQUIRED", "Electrical hazard resolution also requires TT-12 isolation.", 422);
        var types = await store.List<TaskType>(type => codes.Contains(type.Code), ct);
        if (types.Count != codes.Length || types.Any(type => type.RequiredSpecialtyId == Guid.Empty || !IncidentTaskCategories.Contains(type.SourceCategory)))
            throw new ApplicationException("TASK_TYPE_INVALID", "Choose valid corrective Task types from the approved catalog.", 422);
        if (!IncidentTaskTypeCodes.TryGetValue(incidentTypeCode, out var allowed) || codes.Any(code => !allowed.Contains(code)))
            throw new ApplicationException("TASK_TYPE_INVALID_FOR_INCIDENT", "The confirmed Task type does not match this Incident type.", 422);
        return types.OrderBy(type => type.Code).ToList();
    }

    private static string? ValidateScenario(string? scenario, string category, string? incidentTypeCode)
    {
        var code = scenario?.Trim().ToUpperInvariant();
        if (code is null) return null;
        if (code != "EXPOSED_ELECTRICAL_WIRE" || category != PotentialIncident
            || incidentTypeCode?.Trim().ToUpperInvariant() != "SAFETY_HAZARD")
            throw new ApplicationException("SCENARIO_INVALID", "The confirmed scenario is not valid for this report.", 422);
        return code;
    }

    private async Task CreateCorrectiveTasks(Incident incident, IReadOnlyList<TaskType> taskTypes, CancellationToken ct)
    {
        var incidentSnapshot = IncidentSlaPolicyService.ReadSnapshot(incident.SlaSnapshotJson)
            ?? throw new ApplicationException("INCIDENT_SLA_POLICY_INVALID", "Incident SLA snapshot is unavailable.", 503);
        foreach (var type in taskTypes)
        {
            var now = clock.UtcNow;
            var snapshot = incidentSnapshot with { AppliedAt = now,
                ResponseDeadlineAt = now.AddSeconds(incidentSnapshot.ResponseTargetSec),
                ResolutionDeadlineAt = now.AddSeconds(incidentSnapshot.ResolutionTargetSec) };
            var task = new WorkTask { TaskId = Guid.NewGuid(), IncidentId = incident.IncidentId, ZoneId = incident.ZoneId,
                OriginType = TaskOrigin.Incident, TaskTypeId = type.TaskTypeId, IsMandatory = true,
                Title = type.Name.Length <= 200 ? type.Name : type.Name[..200], Description = incident.Description,
                Priority = snapshot.PriorityCode, Status = WorkTaskStatus.Offered,
                SlaPolicyId = snapshot.PolicyId, SlaSnapshotJson = JsonSerializer.Serialize(snapshot, IncidentSlaPolicyService.JsonOptions),
                ResponseDeadlineAt = snapshot.ResponseDeadlineAt, ResolutionDeadlineAt = snapshot.ResolutionDeadlineAt };
            await store.Add(task, ct);
            await dispatch.OfferIncidentTask(task, ct);
        }
    }

    private static string? ValidateDecision(string? decision)
    {
        if (decision is null) return null;
        if (decision is not (AlreadyReported or DifferentIncident))
            throw new ApplicationException("DUPLICATE_DECISION_INVALID", "Use ALREADY_REPORTED or DIFFERENT_INCIDENT.", 422);
        return decision;
    }

    private static string ValidateCategory(string? category)
    {
        var normalized = category?.Trim().ToUpperInvariant();
        if (normalized is not (RoutineRequest or PotentialIncident))
            throw new ApplicationException("REPORT_CATEGORY_INVALID", "Use ROUTINE_REQUEST or POTENTIAL_INCIDENT.", 422);
        return normalized;
    }

    private async Task<string?> AssignSlaOrException(Incident incident, CancellationToken ct)
    {
        try { await incidentSla.Assign(incident, ct); return null; }
        catch (ApplicationException error) when (error.Code is "INCIDENT_SLA_POLICY_MISSING" or "INCIDENT_SLA_POLICY_AMBIGUOUS" or "INCIDENT_SLA_POLICY_INVALID")
        {
            incident.StatusReason = error.Code;
            return error.Code;
        }
    }

    private static string? ReadDecision(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("duplicateDecision", out var value) ? value.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static string? ReadRoutineDecision(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("routineTaskDecision", out var value) ? value.GetString() : null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return null; }
    }

    private static string[] ReadTaskCodes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("confirmedTaskTypeCodes", out var values) || values.ValueKind != JsonValueKind.Array) return [];
            return values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString() ?? "").OrderBy(value => value).ToArray();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return []; }
    }

    private static string? ReadScenarioCode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("confirmedScenarioCode", out var value) ? value.GetString() : null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return null; }
    }

    private void CheckReader(Report report)
    {
        if (report.ReportedByUserId != current.UserId && current.Role is not ("OPERATOR" or "ADMIN"))
            throw new ApplicationException("FORBIDDEN", "You cannot read this report.", 403);
    }

    private static StaffReportView View(Report report, string? code, IEnumerable<StaffReportMedium> rows, Guid? incidentId)
        => new(report.ReportId, report.ZoneId, report.IncidentTypeId, code, report.Title, report.Description,
            report.LocationDescription, report.Status, report.ConfirmedAt,
            rows.Select(x => new StaffReportMediaView(x.MediaId, x.ContentType, x.SizeBytes,
                $"/api/staff-reports/{report.ReportId}/media/{x.MediaId}")).ToArray(), incidentId)
        { ReportCategory = report.ReportCategory, ConfirmedTaskTypeCodes = ReadTaskCodes(report.OriginalInputJson),
            ConfirmedScenarioCode = ReadScenarioCode(report.OriginalInputJson) };
}
