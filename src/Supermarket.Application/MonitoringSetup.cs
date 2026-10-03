using Supermarket.Domain;
namespace Supermarket.Application;

public sealed partial class MonitoringSetup(ISetupStore store, ICurrentUser current,IMonitoringIncidentQueries? runtimeQueries=null,IMonitoringSessionOwnership? ownership=null)
{
    public async Task<IncidentTypeView[]> IncidentTypes(CancellationToken ct)
    {
        UseCase.Admin(current);
        return (await store.List<IncidentType>(t => t.SourceType == "AI_DETECTED", ct)).OrderBy(t => t.Code).Select(t =>
        {
            var d = MonitoringPolicy.Definition(t);
            var supported=t.Code=="LONG_QUEUE" && t.MeasurementType=="QUEUE_LENGTH";
            var options=t.Code=="OVERCROWDING_CONGESTION" && t.MeasurementType=="CROWD_DENSITY"
                ? new[] { new MonitoringMeasurementOptionView("PEOPLE_COUNT","PEOPLE",true,"Temporary count mode; enter whole-person thresholds."),new MonitoringMeasurementOptionView("CROWD_DENSITY","PEOPLE_PER_M2",false,"Physical density runtime is deferred.",d?.Warning,d?.Critical) }
                : new[] { new MonitoringMeasurementOptionView(t.MeasurementType??"UNSUPPORTED",d?.Unit??"PENDING",supported,supported?null:"Measurement runtime is not yet implemented.",d?.Warning,d?.Critical) };
            return new IncidentTypeView(t.IncidentTypeId,t.Code,t.Name,t.Description,t.SourceType,t.MeasurementType,t.Status,
                supported,d?.Unit,d?.Warning,d?.Critical,supported?null:"Select a supported measurement mode or keep this rule disabled in Draft.",options);
        }).ToArray();
    }
    public async Task<MonitoringConfigurationView> Get(Guid zoneId, CancellationToken ct)
    {
        UseCase.Admin(current);
        UseCase.Found(await store.Find<Zone>(zoneId, ct));
        return await View(await Configuration(zoneId, ct), ct);
    }
    public Task<MonitoringConfigurationView> Save(Guid zoneId, MonitoringRequest request, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            UseCase.Found(await store.Find<Zone>(zoneId, ct));
            var existing = (await store.List<MonitoringConfiguration>(m => m.ZoneId == zoneId, ct)).SingleOrDefault();
            if (existing?.Status == "ACTIVE") throw new ApplicationException("MONITORING_ACTIVE", "Deactivate monitoring before changing its configuration.");
            if (existing is not null) CheckVersion(existing, request.ExpectedUpdatedAt);
            else if (request.ExpectedUpdatedAt is not null) throw new ApplicationException("CONFIGURATION_CHANGED", "Configuration changed; reload before saving.");
            MonitoringPolicy.Confidence(request.ConfidenceThreshold);
            var name = Rules.Text(request.Name, 100, "Name");
            var input = request.Rules ?? throw new ApplicationException("RULES_REQUIRED", "Provide the complete rules array; use [] for an empty Draft.", 400);
            Rules.Require(input.Length <= 32, "TOO_MANY_RULES", "A configuration supports at most 32 rules.");
            Rules.Require(input.All(r => r is not null), "INVALID_RULE", "Rules cannot contain null entries.");
            Rules.Require(input.Select(r => r.IncidentTypeId).Distinct().Count() == input.Length, "DUPLICATE_RULE", "Only one rule per incident type is allowed.");
            var configuration = existing ?? new MonitoringConfiguration { ConfigId = Guid.NewGuid(), ZoneId = zoneId, CreatedByUserId = current.UserId };
            var oldRules = existing is null ? new List<MonitoringRule>() : await store.List<MonitoringRule>(r => r.ConfigId == configuration.ConfigId, ct);
            if(runtimeQueries is not null && oldRules.Any(r=>!input.Any(x=>x.IncidentTypeId==r.IncidentTypeId)))
                await runtimeQueries.EnsureSchema(ct); // Refuse deletion before evidence can be safely detached.
            var prepared = new List<(MonitoringRule Rule, bool IsNew)>();
            foreach (var r in input)
            {
                var type = UseCase.Found(await store.Find<IncidentType>(r.IncidentTypeId, ct));
                var old = oldRules.SingleOrDefault(x => x.IncidentTypeId == r.IncidentTypeId);
                var rule = new MonitoringRule { RuleId = old?.RuleId ?? Guid.NewGuid(), ConfigId = configuration.ConfigId,
                    IncidentTypeId = r.IncidentTypeId, WarningThreshold = r.WarningThreshold, CriticalThreshold = r.CriticalThreshold,
                    ThresholdUnit = r.ThresholdUnit, SustainSec = r.SustainSec, CooldownSec = r.CooldownSec,
                    Enabled = r.Enabled, ParametersJson = r.ParametersJson, CreatedAt = old?.CreatedAt ?? default, UpdatedAt = old?.UpdatedAt ?? default };
                MonitoringPolicy.Validate(rule, type);
                prepared.Add((rule, old is null));
            }
            configuration.Name = name;
            configuration.ConfidenceThreshold = request.ConfidenceThreshold;
            configuration.Status = "DRAFT";
            if (existing is null) await store.Add(configuration, ct); else await store.Update(configuration, ct);
            foreach (var old in oldRules.Where(r => !input.Any(x => x.IncidentTypeId == r.IncidentTypeId)))
            {
                // Keep immutable aggregate evidence when its optional rule is removed.
                foreach(var evidence in await store.List<OperationalEvent>(e=>e.RuleId==old.RuleId,ct))
                { evidence.RuleId=null; await store.Update(evidence,ct); }
                await store.Remove(old, ct);
            }
            foreach (var (rule, isNew) in prepared)
                if (isNew) await store.Add(rule, ct); else await store.Update(rule, ct);
            return await View(configuration, ct, prepared.Select(r => r.Rule).ToArray());
        }, ct);
    }
    public Task<MonitoringReviewView> Review(Guid zoneId, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(() => BuildReview(zoneId, ct), ct);
    }
    public Task<MonitoringConfigurationView> Activate(Guid zoneId, bool active, CancellationToken ct) => Activate(zoneId, active, null, ct);
    public Task<MonitoringConfigurationView> Activate(Guid zoneId, bool active, DateTime? expectedUpdatedAt, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            var configuration = await Configuration(zoneId, ct);
            if (expectedUpdatedAt is not null) CheckVersion(configuration, expectedUpdatedAt);
            if (active)
            {
                if(runtimeQueries is not null) await runtimeQueries.EnsureSchema(ct);
                var review = await BuildReview(zoneId, ct);
                if (!review.CanActivate) throw new ApplicationException("MONITORING_NOT_READY", string.Join(" ", review.Issues.Select(i => i.Message)));
                foreach(var camera in review.Cameras.Where(c=>c.Ready))
                {
                    if(ownership?.IsMonitoringOwned(camera.CameraId)!=true) continue;
                    var mappedZones=(await store.List<CameraZoneMapping>(m=>m.CameraId==camera.CameraId && m.Status=="ACTIVE",ct)).Select(m=>m.ZoneId).ToArray();
                    if(!(await store.List<MonitoringConfiguration>(c=>c.Status=="ACTIVE" && mappedZones.Contains(c.ZoneId),ct)).Any())
                        throw new ApplicationException("MONITORING_STOP_PENDING","Monitoring owner is stopping. Wait for STOPPED runtime before reactivating to replay.");
                }
            }
            configuration.Status = active ? "ACTIVE" : "INACTIVE";
            await store.Update(configuration, ct);
            return await View(configuration, ct);
        }, ct);
    }
    private async Task<MonitoringConfiguration> Configuration(Guid zoneId, CancellationToken ct)
        => UseCase.Found((await store.List<MonitoringConfiguration>(m => m.ZoneId == zoneId, ct)).SingleOrDefault());
    private async Task<MonitoringConfigurationView> View(MonitoringConfiguration configuration, CancellationToken ct, MonitoringRule[]? rows = null)
    {
        rows ??= (await store.List<MonitoringRule>(r => r.ConfigId == configuration.ConfigId, ct)).ToArray();
        var types = (await store.List<IncidentType>(ct: ct)).ToDictionary(t => t.IncidentTypeId);
        var views = rows.Select(r => new MonitoringRuleView(r.RuleId,r.IncidentTypeId,
            types.GetValueOrDefault(r.IncidentTypeId)?.Code ?? "UNKNOWN",types.GetValueOrDefault(r.IncidentTypeId)?.Name ?? "Unknown type",
            r.WarningThreshold,r.CriticalThreshold,r.ThresholdUnit,r.SustainSec,r.CooldownSec,r.Enabled,r.ParametersJson)).OrderBy(r => r.IncidentCode).ToArray();
        return new(configuration.ConfigId,configuration.ZoneId,configuration.Name,configuration.ConfidenceThreshold,configuration.Status,
            configuration.CreatedByUserId,configuration.CreatedAt,configuration.UpdatedAt,views);
    }
    private static void CheckVersion(MonitoringConfiguration configuration, DateTime? expected)
    {
        if (expected is null || expected.Value.ToUniversalTime() != configuration.UpdatedAt.ToUniversalTime())
            throw new ApplicationException("CONFIGURATION_CHANGED", "Configuration changed; reload and review before applying changes.");
    }
}
