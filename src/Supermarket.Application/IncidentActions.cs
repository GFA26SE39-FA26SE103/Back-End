using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Supermarket.Domain;

namespace Supermarket.Application;

public sealed record IncidentDismissRequest([Required, MaxLength(2000)] string Reason);

public sealed record IncidentActionView(Guid IncidentId, string Status, string? StatusReason,
    DateTime? ClosedAt, DateTime? MitigatedAt, DateTime UpdatedAt);

public sealed class IncidentActions(ISetupStore store, ICurrentUser current, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] EndedStatuses = ["CLOSED", "DISMISSED", "CANCELLED"];

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
