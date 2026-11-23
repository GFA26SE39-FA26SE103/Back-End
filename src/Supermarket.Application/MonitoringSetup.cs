using Supermarket.Domain;
namespace Supermarket.Application;

public sealed partial class MonitoringSetup(ISetupStore store, ICurrentUser current,IMonitoringIncidentQueries? runtimeQueries=null,IMonitoringSessionOwnership? ownership=null,
    CheckoutMonitoringOptions? checkoutOptions=null,ICheckoutSchema? checkoutSchema=null,ICheckoutConfigurationQueries? checkoutQueries=null)
{
    private readonly CheckoutMonitoringOptions checkout=checkoutOptions??new();
    public async Task<IncidentTypeView[]> IncidentTypes(CancellationToken ct)
    {
        UseCase.Admin(current);
        return (await store.List<IncidentType>(ct: ct)).OrderBy(t => t.Code)
            .Select(t => new IncidentTypeView(t.IncidentTypeId, t.Code, t.Name, t.Description,
                "STAFF_REPORTED", null, t.Status, true, null, null, null, null, null,
                t.RequiresBeforePhoto)).ToArray();
    }
    public async Task<MonitoringEventTypeView[]> EventTypes(CancellationToken ct)
    {
        UseCase.Admin(current);
        return (await store.List<OperationalEventType>(ct: ct)).OrderBy(t => t.Code).Select(t =>
        {
            var definition = MonitoringPolicy.Definition(t);
            var unit = t.Code == "CHECKOUT_CAPACITY" ? "PEOPLE_PER_STAFFED_COUNTER" : definition?.Unit;
            var congestion = MonitoringPolicy.SupportsCongestion(t);
            var checkoutMode = (t.Code, t.MeasurementType) is
                ("LONG_QUEUE", "QUEUE_LENGTH") or ("EXCESSIVE_WAIT", "WAITING_TIME") or ("CHECKOUT_CAPACITY", "CHECKOUT_CAPACITY");
            var supported = t.Status == "ACTIVE" && (congestion || checkoutMode && checkout.AiContractEnabled);
            var options = congestion
                ? new[] {
                    new MonitoringMeasurementOptionView("PEOPLE_COUNT", "PEOPLE", supported,
                        "Counts people currently detected inside the ROI.", definition?.Warning, definition?.Critical),
                    new MonitoringMeasurementOptionView(MonitoringPolicy.DensityMode, MonitoringPolicy.DensityUnit, supported,
                        "Divides the current ROI count by the measured zone area.",
                        MonitoringPolicy.CrowdDensity.Warning, MonitoringPolicy.CrowdDensity.Critical)
                }
                : new[] { new MonitoringMeasurementOptionView(t.MeasurementType, unit ?? "PENDING", supported,
                    supported ? null : checkoutMode && !checkout.AiContractEnabled ? "Checkout AI is disabled in this environment." : "Measurement runtime is unavailable.", definition?.Warning, definition?.Critical) };
            return new MonitoringEventTypeView(t.EventTypeId, t.Code, t.Name, t.Description, t.MeasurementType,
                t.Status, supported, unit, definition?.Warning, definition?.Critical,
                supported ? null : checkoutMode && !checkout.AiContractEnabled ? "Checkout AI is disabled in this environment." : "Measurement runtime is unavailable.", options);
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
            if(runtimeQueries is not null) await runtimeQueries.EnsureSchema(ct);
            UseCase.Found(await store.Find<Zone>(zoneId, ct));
            var existing = (await store.List<MonitoringConfiguration>(m => m.ZoneId == zoneId, ct)).SingleOrDefault();
            if (existing?.Status == "ACTIVE") throw new ApplicationException("MONITORING_ACTIVE", "Deactivate monitoring before changing its configuration.");
            if (existing is not null) CheckVersion(existing, request.ExpectedUpdatedAt);
            else if (request.ExpectedUpdatedAt is not null) throw new ApplicationException("CONFIGURATION_CHANGED", "Configuration changed; reload before saving.");
            MonitoringPolicy.Confidence(request.ConfidenceThreshold);
            var name = Rules.Text(request.Name, 100, "Name");
            var input = request.Rules ?? throw new ApplicationException("RULES_REQUIRED", "Add at least one operational event rule before saving the AI configuration.", 400);
            Rules.Require(input.Length > 0, "RULES_REQUIRED", "Add at least one operational event rule before saving the AI configuration.");
            Rules.Require(input.Length <= 32, "TOO_MANY_RULES", "A configuration supports at most 32 rules.");
            Rules.Require(input.All(r => r is not null), "INVALID_RULE", "Rules cannot contain null entries.");
            Rules.Require(input.Select(r => r.EventTypeId).Distinct().Count() == input.Length, "DUPLICATE_RULE", "Only one rule per event type is allowed.");
            var configuration = existing ?? new MonitoringConfiguration { ConfigId = Guid.NewGuid(), ZoneId = zoneId, CreatedByUserId = current.UserId };
            var oldRules = existing is null ? new List<MonitoringRule>() : await store.List<MonitoringRule>(r => r.ConfigId == configuration.ConfigId, ct);
            var prepared = new List<(MonitoringRule Rule, bool IsNew)>();
            foreach (var r in input)
            {
                var eventType = UseCase.Found(await store.Find<OperationalEventType>(r.EventTypeId, ct));
                Rules.Require(eventType.Status == "ACTIVE", "OPERATIONAL_EVENT_TYPE_INACTIVE", "An inactive event type cannot be added to monitoring.");
                var old = oldRules.SingleOrDefault(x => x.OutcomeType=="OPERATIONAL_EVENT" && x.EventTypeId == eventType.EventTypeId);
                var rule = new MonitoringRule { RuleId = old?.RuleId ?? Guid.NewGuid(), ConfigId = configuration.ConfigId,
                    EventTypeId=eventType.EventTypeId, OutcomeType="OPERATIONAL_EVENT",
                    WarningThreshold = r.WarningThreshold, CriticalThreshold = r.CriticalThreshold,
                    ThresholdUnit = r.ThresholdUnit, SustainSec = r.SustainSec, CooldownSec = r.CooldownSec,
                    ClearDurationSec = r.ClearDurationSec,
                    Enabled = r.Enabled, ParametersJson = RuleHysteresis.Merge(r.ParametersJson, r.WarningExitThreshold, r.CriticalExitThreshold), CreatedAt = old?.CreatedAt ?? default, UpdatedAt = old?.UpdatedAt ?? default };
                MonitoringPolicy.Validate(rule, eventType, requireDualThresholds: true);
                if (old is not null && MonitoringPolicy.SupportsCongestion(eventType) && old.ThresholdUnit != rule.ThresholdUnit
                    && (await store.List<OperationalEvent>(e => e.RuleId == old.RuleId && e.ClearedAt == null, ct)).Count > 0)
                    throw new ApplicationException("OPEN_CONGESTION_EVENT", "Resolve the open congestion event before changing the measurement unit.");
                prepared.Add((rule, old is null));
            }
            var retainedRuleIds=prepared.Select(x=>x.Rule.RuleId).ToHashSet();
            if(runtimeQueries is not null && oldRules.Any(r=>!retainedRuleIds.Contains(r.RuleId)))
                await runtimeQueries.EnsureSchema(ct); // Refuse deletion before evidence can be safely detached.
            configuration.Name = name;
            configuration.ConfidenceThreshold = request.ConfidenceThreshold;
            configuration.Status = "DRAFT";
            if (existing is null) await store.Add(configuration, ct); else await store.Update(configuration, ct);
            foreach (var old in oldRules.Where(r => !retainedRuleIds.Contains(r.RuleId)))
                await RemoveRulePreservingHistory(old, ct);
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
            var rules = await store.List<MonitoringRule>(r => r.ConfigId == configuration.ConfigId, ct);
            if (runtimeQueries is not null && rules.Count > 0) await runtimeQueries.EnsureSchema(ct);
            foreach (var rule in rules) await RemoveRulePreservingHistory(rule, ct);
            // Retain historical events; remove only their optional rule references.
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
                if(runtimeQueries is not null) await runtimeQueries.EnsureSchema(ct);
                var review = await BuildReview(zoneId, ct);
                if (!review.CanActivate) throw new ApplicationException("MONITORING_NOT_READY", string.Join(" ", review.Issues.Select(i => i.Message)));
                foreach(var camera in review.Cameras.Where(c=>c.Ready).DistinctBy(c=>c.CameraId))
                {
                    if(ownership?.IsMonitoringOwned(camera.CameraId)!=true) continue;
                    if(!await HasActiveMonitoringOwner(camera.CameraId,ct))
                        throw new ApplicationException("MONITORING_STOP_PENDING","Monitoring owner is stopping. Wait for STOPPED runtime before reactivating to replay.");
                }
            }
            configuration.Status = active ? "ACTIVE" : "INACTIVE";
            await store.Update(configuration, ct);
            return await View(configuration, ct);
        }, ct);
    }
    private async Task<bool> HasActiveMonitoringOwner(Guid cameraId,CancellationToken ct)
    {
        var mappedZones=(await store.List<CameraZoneMapping>(m=>m.CameraId==cameraId && m.Status=="ACTIVE",ct)).Select(m=>m.ZoneId).ToHashSet();
        var active=await store.List<MonitoringConfiguration>(c=>c.Status=="ACTIVE",ct);
        if(active.Any(c=>mappedZones.Contains(c.ZoneId))) return true; // Preserve generic ownership.
        if(active.Count==0) return false;
        var eventTypes=await store.List<OperationalEventType>(t=>t.Code=="CHECKOUT_CAPACITY",ct);
        if(eventTypes.Count!=1) return false;
        var typeId=eventTypes[0].EventTypeId;
        var configIds=active.Select(c=>c.ConfigId).ToArray();
        var checkoutRules=await store.List<MonitoringRule>(r=>r.Enabled && r.OutcomeType=="OPERATIONAL_EVENT"
            && r.EventTypeId==typeId && configIds.Contains(r.ConfigId),ct);
        CheckoutSnapshotPreparation.Limit(checkoutRules.Count,128,128,"Active checkout owner rules");
        if(checkoutRules.Count==0) return false; // Generic-only configurations never query checkout tables/schema.
        if(checkoutSchema is null || !await checkoutSchema.Ready(ct))
            throw new ApplicationException("CHECKOUT_SCHEMA_NOT_READY","Checkout configuration schema is not ready; monitoring remains disabled.",503);
        var checkoutConfigIds=checkoutRules.Select(r=>r.ConfigId).ToHashSet();
        var checkoutZones=active.Where(c=>checkoutConfigIds.Contains(c.ConfigId)).Select(c=>c.ZoneId).ToHashSet();
        CheckoutSnapshotPreparation.Limit(checkoutZones.Count,checkout.MaxZones,128,"Active checkout zones");
        var mappings=await CheckoutSnapshotPreparation.CameraMappings(store,checkoutQueries,checkout,cameraId,ct);
        foreach(var counterId in mappings.Select(m=>m.CheckoutCounterId).Distinct())
            if(await store.Find<CheckoutCounter>(counterId,ct) is { IsEnabled:true } counter && checkoutZones.Contains(counter.ZoneId)) return true;
        return false;
    }
    private async Task<MonitoringConfiguration> Configuration(Guid zoneId, CancellationToken ct)
        => UseCase.Found((await store.List<MonitoringConfiguration>(m => m.ZoneId == zoneId, ct)).SingleOrDefault());
    private async Task RemoveRulePreservingHistory(MonitoringRule rule, CancellationToken ct)
    {
        foreach (var evidence in await store.List<OperationalEvent>(e => e.RuleId == rule.RuleId, ct))
        {
            evidence.RuleId = null;
            await store.Update(evidence, ct);
        }
        await store.Remove(rule, ct);
    }
    private async Task<MonitoringConfigurationView> View(MonitoringConfiguration configuration, CancellationToken ct, MonitoringRule[]? rows = null)
    {
        rows ??= (await store.List<MonitoringRule>(r => r.ConfigId == configuration.ConfigId, ct)).ToArray();
        var eventTypes = (await store.List<OperationalEventType>(ct: ct)).ToDictionary(t => t.EventTypeId);
        return ConfigurationView(configuration, rows, eventTypes);
    }
    internal static MonitoringConfigurationView ConfigurationView(MonitoringConfiguration configuration, MonitoringRule[] rows,
        IReadOnlyDictionary<Guid, OperationalEventType> eventTypes)
    {
        var views = rows.Select(r =>
        {
            var eventType=r.EventTypeId is { } eventTypeId ? eventTypes.GetValueOrDefault(eventTypeId) : null;
            var (warningExit,criticalExit)=RuleHysteresis.Read(r.ParametersJson);
            return new MonitoringRuleView(r.RuleId,eventType?.EventTypeId??Guid.Empty,
                eventType?.Code??"UNKNOWN",eventType?.Name??"Unknown event type",
                r.WarningThreshold,r.CriticalThreshold,r.ThresholdUnit,r.SustainSec,r.CooldownSec,r.ClearDurationSec,r.Enabled,r.ParametersJson,
                warningExit,criticalExit);
        }).OrderBy(r => r.EventCode).ToArray();
        return new(configuration.ConfigId,configuration.ZoneId,configuration.Name,configuration.ConfidenceThreshold,configuration.Status,
            configuration.CreatedByUserId,configuration.CreatedAt,configuration.UpdatedAt,views);
    }
    private static void CheckVersion(MonitoringConfiguration configuration, DateTime? expected)
    {
        if (expected is null || expected.Value.ToUniversalTime() != configuration.UpdatedAt.ToUniversalTime())
            throw new ApplicationException("CONFIGURATION_CHANGED", "Configuration changed; reload and review before applying changes.");
    }
}
