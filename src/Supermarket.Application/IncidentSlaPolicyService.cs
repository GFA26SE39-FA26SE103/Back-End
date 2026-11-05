using System.Text.Json;
using Supermarket.Domain;

namespace Supermarket.Application;

public sealed record IncidentSlaSnapshot(
    Guid PolicyId,
    int PolicyVersion,
    Guid IncidentTypeId,
    string PriorityCode,
    int ResponseTargetSec,
    int ResolutionTargetSec,
    int OfferTimeoutSec,
    int WarningPercent,
    DateTime AppliedAt,
    DateTime ResponseDeadlineAt,
    DateTime ResolutionDeadlineAt);

public sealed class IncidentSlaPolicyService(ISetupStore store, IClock clock)
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    public async Task<IncidentSlaSnapshot> Assign(Incident incident, CancellationToken ct)
    {
        if (incident.SlaPolicyId is not null || incident.SlaSnapshotJson is not null || incident.ResolutionDeadlineAt is not null)
            throw new ApplicationException("INCIDENT_SLA_ALREADY_ASSIGNED", "The Incident already has an SLA snapshot.");

        var matches = await store.List<SlaPolicy>(p => p.IsEnabled
            && p.ProblemDomain == "INCIDENT"
            && p.IncidentTypeId == incident.IncidentTypeId, ct);
        if (matches.Count == 0)
            throw new ApplicationException("INCIDENT_SLA_POLICY_MISSING", "No enabled SLA policy is configured for this Incident type.", 422);
        if (matches.Count > 1)
            throw new ApplicationException("INCIDENT_SLA_POLICY_AMBIGUOUS", "Multiple enabled SLA policies match this Incident type.", 503);

        var policy = matches[0];
        Validate(policy, incident.IncidentTypeId);
        var appliedAt = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        var snapshot = new IncidentSlaSnapshot(
            policy.PolicyId,
            policy.Version,
            incident.IncidentTypeId,
            policy.PriorityCode,
            policy.ResponseTargetSec,
            policy.ResolutionTargetSec,
            policy.OfferTimeoutSec,
            policy.WarningPercent,
            appliedAt,
            appliedAt.AddSeconds(policy.ResponseTargetSec),
            appliedAt.AddSeconds(policy.ResolutionTargetSec));

        incident.SlaPolicyId = policy.PolicyId;
        incident.SlaSnapshotJson = JsonSerializer.Serialize(snapshot, JsonOptions);
        incident.ResolutionDeadlineAt = snapshot.ResolutionDeadlineAt;
        return snapshot;
    }

    public static IncidentSlaSnapshot? ReadSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<IncidentSlaSnapshot>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static void Validate(SlaPolicy policy, Guid incidentTypeId)
    {
        var validPriority = policy.PriorityCode is "LOW" or "MEDIUM" or "HIGH" or "CRITICAL";
        if (policy.PolicyId == Guid.Empty || policy.Version <= 0 || policy.ProblemDomain != "INCIDENT"
            || policy.IncidentTypeId != incidentTypeId || !validPriority
            || policy.ResponseTargetSec <= 0 || policy.ResolutionTargetSec < policy.ResponseTargetSec
            || policy.OfferTimeoutSec <= 0 || policy.WarningPercent is <= 0 or >= 100)
            throw new ApplicationException("INCIDENT_SLA_POLICY_INVALID", "The matched Incident SLA policy is invalid.", 503);
    }
}
