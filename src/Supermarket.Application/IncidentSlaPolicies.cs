using Supermarket.Domain;

namespace Supermarket.Application;

public sealed record IncidentSlaPolicyView(Guid PolicyId, Guid IncidentTypeId, string IncidentTypeCode, string IncidentTypeName,
    bool IsEnabled, int Version, string PriorityCode, int ResponseTargetSec, int ResolutionTargetSec, int OfferTimeoutSec,
    int WarningPercent, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record IncidentSlaPolicySaveRequest(int? ExpectedVersion, bool IsEnabled, string PriorityCode, int ResponseTargetSec,
    int ResolutionTargetSec, int OfferTimeoutSec, int WarningPercent);

public sealed class IncidentSlaPolicies(ISetupStore store, ICurrentUser current)
{
    public async Task<IncidentSlaPolicyView[]> List(CancellationToken ct)
    {
        UseCase.Admin(current);
        var types = (await store.List<IncidentType>(ct: ct)).ToDictionary(x => x.IncidentTypeId);
        return (await store.List<SlaPolicy>(p => p.ProblemDomain == "INCIDENT" && p.IncidentTypeId != null, ct))
            .Where(p => types.ContainsKey(p.IncidentTypeId!.Value)).OrderBy(p => types[p.IncidentTypeId!.Value].Code)
            .Select(p => View(p, types[p.IncidentTypeId!.Value])).ToArray();
    }

    public Task<IncidentSlaPolicyView> Save(Guid incidentTypeId, IncidentSlaPolicySaveRequest request, CancellationToken ct)
    {
        UseCase.Admin(current);
        Validate(request);
        return store.Transaction(async () =>
        {
            var type = UseCase.Found(await store.Find<IncidentType>(incidentTypeId, ct));
            var matches = await store.List<SlaPolicy>(p => p.ProblemDomain == "INCIDENT" && p.IncidentTypeId == incidentTypeId, ct);
            if (matches.Count > 1) throw new ApplicationException("SLA_POLICY_AMBIGUOUS", "More than one Incident SLA policy exists for this IncidentType.", 503);
            var policy = matches.SingleOrDefault();
            if (policy is null)
            {
                if (request.ExpectedVersion is not null) throw VersionConflict();
                policy = new SlaPolicy { PolicyId = Guid.NewGuid(), ProblemDomain = "INCIDENT", IncidentTypeId = incidentTypeId, Version = 1 };
                Apply(policy, request);
                await store.Add(policy, ct);
            }
            else
            {
                if (request.ExpectedVersion != policy.Version) throw VersionConflict();
                policy.Version++;
                Apply(policy, request);
                await store.Update(policy, ct);
            }
            return View(policy, type);
        }, ct);
    }

    private static void Validate(IncidentSlaPolicySaveRequest request)
    {
        Rules.Status(request.PriorityCode, "LOW", "MEDIUM", "HIGH", "CRITICAL");
        Rules.Require(request.ResponseTargetSec > 0 && request.ResolutionTargetSec >= request.ResponseTargetSec && request.OfferTimeoutSec > 0,
            "INVALID_SLA_TARGETS", "SLA targets must be positive and resolution cannot be earlier than response.");
        Rules.Require(request.WarningPercent is > 0 and < 100, "INVALID_SLA_WARNING", "Warning percent must be between 1 and 99.");
    }
    private static void Apply(SlaPolicy policy, IncidentSlaPolicySaveRequest request)
    {
        policy.IsEnabled = request.IsEnabled; policy.PriorityCode = request.PriorityCode; policy.ResponseTargetSec = request.ResponseTargetSec;
        policy.ResolutionTargetSec = request.ResolutionTargetSec; policy.OfferTimeoutSec = request.OfferTimeoutSec; policy.WarningPercent = request.WarningPercent;
        policy.EventTypeId = null;
    }
    private static IncidentSlaPolicyView View(SlaPolicy p, IncidentType t) => new(p.PolicyId, t.IncidentTypeId, t.Code, t.Name,
        p.IsEnabled, p.Version, p.PriorityCode, p.ResponseTargetSec, p.ResolutionTargetSec, p.OfferTimeoutSec, p.WarningPercent, p.CreatedAt, p.UpdatedAt);
    private static ApplicationException VersionConflict() => new("SLA_POLICY_VERSION_CONFLICT", "The SLA policy changed; reload and retry.");
}
