using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Supermarket.Domain;

namespace Supermarket.Application;

public sealed record IncidentDismissRequest([Required, MaxLength(2000)] string Reason);

public sealed record IncidentActionView(Guid IncidentId, string Status, string? StatusReason,
    DateTime? ClosedAt, DateTime? MitigatedAt, DateTime UpdatedAt);

/// <summary>L3: one Task of an Incident; closure waits for every mandatory one to be VERIFIED (BR-22).</summary>
public sealed record IncidentTaskView(Guid TaskId, string Title, string Status, bool IsMandatory, string? VerificationMode, string? AssigneeName, DateTime? VerifiedAt,
    string? TaskTypeCode = null, bool IsMitigation = false);
/// <summary>L3: Operator Incident board row. Mitigation (BR-21) and closure (BR-22) are separate milestones.</summary>
public sealed record IncidentListView(Guid IncidentId, string Title, string? Description, string SourceType, string? IncidentTypeCode, string? IncidentTypeName,
    Guid ZoneId, string ZoneName, string Status, string? StatusReason, DateTime CreatedAt, DateTime UpdatedAt, DateTime? ClosedAt, DateTime? MitigatedAt,
    DateTime? ResolutionDeadlineAt, string? SlaPriorityCode, bool IsOpen, int MandatoryTasks, int VerifiedMandatoryTasks, IncidentTaskView[] Tasks);

public sealed class IncidentActions(ISetupStore store, ICurrentUser current, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] EndedStatuses = ["CLOSED", "DISMISSED", "CANCELLED"];
    public const int ListLimit = 200;

    /// <summary>state = open (default) | ended | all; newest first, at most <see cref="ListLimit"/> rows.</summary>
    public async Task<IncidentListView[]> List(string? state, CancellationToken ct)
    {
        UseCase.Operator(current);
        var filter = string.IsNullOrWhiteSpace(state) ? "open" : state.Trim().ToLowerInvariant();
        Rules.Status(filter, "open", "ended", "all");
        var incidents = filter switch
        {
            "open" => await store.List<Incident>(i => i.ClosedAt == null && !EndedStatuses.Contains(i.Status), ct),
            "ended" => await store.List<Incident>(i => i.ClosedAt != null || EndedStatuses.Contains(i.Status), ct),
            _ => await store.List<Incident>(ct: ct)
        };
        return await Views(incidents.OrderByDescending(i => i.CreatedAt).Take(ListLimit).ToList(), ct);
    }

    public async Task<IncidentListView> Get(Guid incidentId, CancellationToken ct)
    {
        UseCase.Operator(current);
        var incident = UseCase.Found(await store.Find<Incident>(incidentId, ct));
        return (await Views([incident], ct)).Single();
    }

    private async Task<IncidentListView[]> Views(List<Incident> incidents, CancellationToken ct)
    {
        if (incidents.Count == 0) return [];
        var ids = incidents.Select(i => i.IncidentId).ToList();
        var typeIds = incidents.Select(i => i.IncidentTypeId).Distinct().ToList();
        var zoneIds = incidents.Select(i => i.ZoneId).Distinct().ToList();
        var types = (await store.List<IncidentType>(t => typeIds.Contains(t.IncidentTypeId), ct)).ToDictionary(t => t.IncidentTypeId);
        var zones = (await store.List<Zone>(z => zoneIds.Contains(z.ZoneId), ct)).ToDictionary(z => z.ZoneId, z => z.Name);
        var tasks = await store.List<WorkTask>(t => t.IncidentId != null && ids.Contains(t.IncidentId.Value), ct);
        var assigneeIds = tasks.Where(t => t.CurrentAssigneeUserId != null).Select(t => t.CurrentAssigneeUserId!.Value).Distinct().ToList();
        var names = (await store.List<UserAccount>(u => assigneeIds.Contains(u.UserId), ct)).ToDictionary(u => u.UserId, u => u.FullName);
        var taskTypeIds = tasks.Where(t => t.TaskTypeId != null).Select(t => t.TaskTypeId!.Value).Distinct().ToList();
        var taskTypeCodes = (await store.List<TaskType>(t => taskTypeIds.Contains(t.TaskTypeId), ct)).ToDictionary(t => t.TaskTypeId, t => t.Code);
        return incidents.Select(i =>
        {
            var own = tasks.Where(t => t.IncidentId == i.IncidentId).OrderByDescending(t => t.IsMandatory).ThenBy(t => t.CreatedAt)
                .Select(t => new IncidentTaskView(t.TaskId, t.Title, t.Status, t.IsMandatory, t.VerificationMode,
                    t.CurrentAssigneeUserId is { } id ? names.GetValueOrDefault(id) : null, t.VerifiedAt,
                    t.TaskTypeId is { } typeId ? taskTypeCodes.GetValueOrDefault(typeId) : null,
                    // BR-21 / K5: TT-12 is the designated mitigation Task (IncidentMitigation).
                    t.TaskTypeId is { } mitigationTypeId && taskTypeCodes.GetValueOrDefault(mitigationTypeId) == IncidentMitigation.MitigationTaskTypeCode)).ToArray();
            var type = types.GetValueOrDefault(i.IncidentTypeId);
            return new IncidentListView(i.IncidentId, i.Title, i.Description, i.SourceType, type?.Code, type?.Name, i.ZoneId, zones.GetValueOrDefault(i.ZoneId) ?? "",
                i.Status, i.StatusReason, i.CreatedAt, i.UpdatedAt, i.ClosedAt, i.MitigatedAt, i.ResolutionDeadlineAt,
                IncidentSlaPolicyService.ReadSnapshot(i.SlaSnapshotJson)?.PriorityCode, i.ClosedAt is null && !EndedStatuses.Contains(i.Status),
                own.Count(t => t.IsMandatory), own.Count(t => t.IsMandatory && t.Status == WorkTaskStatus.Verified), own);
        }).ToArray();
    }

    public Task<IncidentActionView> Dismiss(Guid incidentId, IncidentDismissRequest request, CancellationToken ct)
    {
        UseCase.Operator(current);
        var reason = request.Reason?.Trim() ?? "";
        if (reason.Length == 0)
            throw new ApplicationException("INCIDENT_DISMISS_REASON_REQUIRED", "A reason is required to dismiss an incident.", 422);
        if (reason.Length > 2000)
            throw new ApplicationException("INCIDENT_DISMISS_REASON_TOO_LONG", "The dismissal reason must be 2000 characters or fewer.", 422);

        return store.Transaction(async () =>
        {
            var incident = UseCase.Found(await store.Find<Incident>(incidentId, ct));
            if (incident.ClosedAt is not null || EndedStatuses.Contains(incident.Status))
                throw new ApplicationException("INCIDENT_ALREADY_ENDED", "The incident has already ended.");

            var before = State(incident);
            var now = clock.UtcNow;
            incident.Status = "DISMISSED";
            incident.StatusReason = reason;
            incident.ClosedAt = now;
            incident.UpdatedAt = now;
            await store.Update(incident, ct);
            await store.Add(new AuditLog
            {
                AuditLogId = Guid.NewGuid(), UserId = current.UserId, Action = "DISMISS_INCIDENT",
                EntityType = "Incident", EntityId = incident.IncidentId,
                OldValuesJson = JsonSerializer.Serialize(before, Json),
                NewValuesJson = JsonSerializer.Serialize(State(incident), Json),
                Reason = reason, CreatedAt = now, ResultStatus = "SUCCESS"
            }, ct);
            return View(incident);
        }, ct);
    }

    private static object State(Incident incident) => new
    {
        incident.Status, incident.StatusReason, incident.ClosedAt, incident.MitigatedAt
    };

    private static IncidentActionView View(Incident incident) => new(incident.IncidentId, incident.Status,
        incident.StatusReason, incident.ClosedAt, incident.MitigatedAt, incident.UpdatedAt);
}
