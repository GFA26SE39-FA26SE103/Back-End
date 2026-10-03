using Supermarket.Domain;
namespace Supermarket.Application;

public sealed partial class MonitoringSetup(ISetupStore store, ICurrentUser current)
{
    public async Task<IncidentTypeView[]> IncidentTypes(CancellationToken ct)
    {
        UseCase.Admin(current);
        return (await store.List<IncidentType>(t => t.SourceType == "AI_DETECTED", ct)).OrderBy(t => t.Code).Select(t =>
        {
            var d = MonitoringPolicy.Definition(t);
            return new IncidentTypeView(t.IncidentTypeId,t.Code,t.Name,t.Description,t.SourceType,t.MeasurementType,t.Status,
                d is not null,d?.Unit,d?.Warning,d?.Critical,d is null ? "Counter/composite measurement is not yet defined. Keep this rule disabled in Draft." : null);
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
            var input = request.Rules ?? throw new ApplicationException("RULES_REQUIRED", "Add at least one incident rule before saving the AI configuration.", 400);
            Rules.Require(input.Length > 0, "RULES_REQUIRED", "Add at least one incident rule before saving the AI configuration.");
            Rules.Require(input.Length <= 32, "TOO_MANY_RULES", "A configuration supports at most 32 rules.");
            Rules.Require(input.All(r => r is not null), "INVALID_RULE", "Rules cannot contain null entries.");
            Rules.Require(input.Select(r => r.IncidentTypeId).Distinct().Count() == input.Length, "DUPLICATE_RULE", "Only one rule per incident type is allowed.");
            var configuration = existing ?? new MonitoringConfiguration { ConfigId = Guid.NewGuid(), ZoneId = zoneId, CreatedByUserId = current.UserId };
            var oldRules = existing is null ? new List<MonitoringRule>() : await store.List<MonitoringRule>(r => r.ConfigId == configuration.ConfigId, ct);
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
            foreach (var old in oldRules.Where(r => !input.Any(x => x.IncidentTypeId == r.IncidentTypeId))) await store.Remove(old, ct);
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
    public Task Delete(Guid zoneId, MonitoringDeleteRequest request, CancellationToken ct)
    {
        UseCase.Admin(current);
        return store.Transaction(async () =>
        {
            var configuration = await Configuration(zoneId, ct);
            if (configuration.ConfigId != request.ConfigId)
                throw new ApplicationException("CONFIGURATION_CHANGED", "Configuration changed; reload before deleting.");
            CheckVersion(configuration, request.ExpectedUpdatedAt);
            if (configuration.Status == "ACTIVE")
                throw new ApplicationException("MONITORING_ACTIVE", "Deactivate monitoring before deleting its configuration.");
            Rules.Status(configuration.Status, "DRAFT", "INACTIVE");
            foreach (var rule in await store.List<MonitoringRule>(r => r.ConfigId == configuration.ConfigId, ct))
                await store.Remove(rule, ct);
            // Delete only the configuration and its rules. Other FK references reject and roll back the transaction.
            await store.Remove(configuration, ct);
            return true;
        }, ct);
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
                var review = await BuildReview(zoneId, ct);
                if (!review.CanActivate) throw new ApplicationException("MONITORING_NOT_READY", string.Join(" ", review.Issues.Select(i => i.Message)));
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
        return ConfigurationView(configuration, rows, types);
    }
    internal static MonitoringConfigurationView ConfigurationView(MonitoringConfiguration configuration, MonitoringRule[] rows, IReadOnlyDictionary<Guid, IncidentType> types)
    {
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
