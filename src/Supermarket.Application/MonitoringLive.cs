using Supermarket.Domain;
namespace Supermarket.Application;

public sealed class MonitoringLive(ISetupStore store,ICurrentUser current,IMonitoringRuntimeState runtime,IMonitoringIncidentQueries queries,IMonitoringSnapshotReader snapshots,
    CheckoutSourceGuard checkoutGuard,ICheckoutRuntimeState checkoutRuntime,IRecordedMonitoringReplay replay)
{
    public async Task<MonitoringCameraRuntimeView> Runtime(Guid cameraId,CancellationToken ct)
    {
        UseCase.LiveView(current); UseCase.Found(await store.Find<Camera>(cameraId,ct)); await queries.EnsureSchema(ct);
        var cached=runtime.Get(cameraId);
        var zones=new List<MonitoringZoneRuntimeView>();
        var mappings=await store.List<CameraZoneMapping>(m=>m.CameraId==cameraId && m.Status=="ACTIVE",ct);
        foreach(var zoneId in mappings.Select(m=>m.ZoneId).Distinct())
        {
            var zone=UseCase.Found(await store.Find<Zone>(zoneId,ct));
            var config=(await store.List<MonitoringConfiguration>(c=>c.ZoneId==zone.ZoneId,ct)).SingleOrDefault();
            var previous=cached?.Zones.SingleOrDefault(z=>z.ZoneId==zone.ZoneId && z.ConfigId==config?.ConfigId && z.ConfigVersion==config?.UpdatedAt);
            if(config?.Status=="ACTIVE" && previous is not null) { zones.Add(previous); continue; }
            var ruleViews=new List<MonitoringRuleRuntimeView>();
            if(config is not null)
                foreach(var rule in await store.List<MonitoringRule>(r=>r.ConfigId==config.ConfigId && r.Enabled,ct))
                {
                    if(rule.OutcomeType=="OPERATIONAL_EVENT")
                    {
                        var type=rule.EventTypeId is { } eventTypeId ? await store.Find<OperationalEventType>(eventTypeId,ct) : null;
                        if(MonitoringPolicy.UsesCheckoutRuntime(type?.Code,zone.ZoneType)) continue;
                        ruleViews.Add(RuleView(rule,type,config.Status));
                    }
                    else
                    {
                        ruleViews.Add(new(rule.RuleId,"UNKNOWN","Invalid monitoring outcome","UNSUPPORTED",rule.ThresholdUnit,rule.WarningThreshold,rule.CriticalThreshold,
                            rule.SustainSec,rule.CooldownSec,rule.ClearDurationSec,null,0,0,0,"MONITORING_OUTCOME_INVALID"));
                    }
                }
            zones.Add(new(zone.ZoneId,zone.Name,config?.ConfigId,config?.UpdatedAt,config?.Status??"NOT_CONFIGURED",config?.ConfidenceThreshold??0,
                null,null,ruleViews.ToArray(), AreaM2: ruleViews.Any(r => r.Mode == MonitoringPolicy.DensityMode) ? zone.AreaM2 : null));
        }
        var active=zones.Where(z=>z.ConfigurationStatus=="ACTIVE").ToArray();
        var checkoutZones=await CheckoutZones(cameraId,ct);
        foreach(var checkout in checkoutZones)
        {
            var index=zones.FindIndex(z=>z.ZoneId==checkout.ZoneId);
            if(index<0) zones.Add(checkout);
            else
            {
                var generic=zones[index];
                zones[index]=generic with { QueueCount=checkout.QueueCount, Rules=[..generic.Rules,..checkout.Rules], IssueCode=generic.IssueCode??checkout.IssueCode };
            }
        }
        var hasCheckout=checkoutZones.Count>0;
        var source=(await store.List<CameraConnection>(c=>c.CameraId==cameraId,ct)).SingleOrDefault()?.SourceType;
        if(active.Length==0 && !hasCheckout)
        {
            // Desired SQL state can be inactive before the next worker tick has
            // released its owner. Do not advertise replay readiness too early.
            var stopping=cached is not null && (cached.HasCheckoutMonitoring || cached.Zones.Any(z=>z.ConfigurationStatus=="ACTIVE"));
            return new(cameraId,stopping?"STOPPING":"STOPPED",stopping?"MONITORING_STOP_PENDING":"NO_ACTIVE_CONFIGURATION",null,
                source,null,null,null,false,null,zones.ToArray());
        }
        if(cached is not null && cached.HasCheckoutMonitoring==hasCheckout
            && active.All(z=>cached.Zones.Any(c=>c.ConfigId==z.ConfigId && c.ConfigVersion==z.ConfigVersion)
                || z.Rules.Length==0 && checkoutZones.Any(c=>c.ZoneId==z.ZoneId && c.ConfigVersion==z.ConfigVersion)))
            return cached with { Zones=zones.ToArray() };
        var snapshot=(await snapshots.ReadAll(ct)).SingleOrDefault(s=>s.CameraId==cameraId);
        if(snapshot?.IssueCode is { } issue) return new(cameraId,"ERROR",issue,issue,source,null,null,null,true,null,zones.ToArray(),hasCheckout);
        return new(cameraId,"STARTING","STARTING",null,source,null,null,null,cached is not null,null,zones.ToArray(),hasCheckout);
    }

    private async Task<List<MonitoringZoneRuntimeView>> CheckoutZones(Guid cameraId,CancellationToken ct)
    {
        var zones=new List<MonitoringZoneRuntimeView>();
        foreach(var config in await checkoutGuard.ActiveConfigurations(cameraId,null,ct))
        {
            var zone=UseCase.Found(await store.Find<Zone>(config.ZoneId,ct));
            if(zone.ZoneType!="CHECKOUT") continue;
            var reading=checkoutRuntime.Get(zone.ZoneId);
            if(reading is not null && (reading.ConfigId!=config.ConfigId || reading.ConfigVersion!=config.UpdatedAt)) reading=null;
            var rules=reading?.Rules;
            if(rules is null)
            {
                var configured=new List<MonitoringRuleRuntimeView>();
                foreach(var rule in await store.List<MonitoringRule>(r=>r.ConfigId==config.ConfigId && r.Enabled && r.OutcomeType=="OPERATIONAL_EVENT",ct))
                {
                    var type=rule.EventTypeId is { } id ? await store.Find<OperationalEventType>(id,ct) : null;
                    if(type is not null && MonitoringPolicy.UsesCheckoutRuntime(type.Code,zone.ZoneType))
                        configured.Add(RuleView(rule,type,config.Status,checkout:true));
                }
                rules=configured.ToArray();
            }
            if(rules.Length==0) continue;
            var count=reading?.QueueCount;
            zones.Add(new(zone.ZoneId,zone.Name,config.ConfigId,config.UpdatedAt,config.Status,config.ConfidenceThreshold,null,
                count is >=0 and <=int.MaxValue ? (int)count.Value : null,rules,reading?.IssueCode));
        }
        return zones;
    }

    private static MonitoringRuleRuntimeView RuleView(MonitoringRule rule,OperationalEventType? type,string configStatus,bool checkout=false)
    {
        var definition=type is null ? new RuntimeMeasurementDefinition("UNSUPPORTED",rule.ThresholdUnit,false,"Operational event type unavailable.")
            : checkout ? MonitoringPolicy.CheckoutRuntimeDefinition(rule,type) : MonitoringPolicy.RuntimeDefinition(rule,type);
        var (warningExit,criticalExit)=RuleHysteresis.Read(rule.ParametersJson);
        return new(rule.RuleId,type?.Code??"UNKNOWN",type?.Name??"Unknown event type",definition.Mode,definition.Unit,rule.WarningThreshold,rule.CriticalThreshold,
            rule.SustainSec,rule.CooldownSec,rule.ClearDurationSec,null,0,0,0,
            configStatus!="ACTIVE"?"NO_ACTIVE_CONFIGURATION":!definition.Supported?"RULE_RUNTIME_UNSUPPORTED":"STARTING",
            WarningExitThreshold:warningExit??rule.WarningThreshold,CriticalExitThreshold:criticalExit??rule.CriticalThreshold);
    }
    public async Task<IncidentFeedView> Incidents(Guid cameraId,IncidentFeedQuery query,CancellationToken ct)
    {
        UseCase.LiveView(current); UseCase.Found(await store.Find<Camera>(cameraId,ct));
        return await queries.PageForCamera(cameraId,query,ct);
    }

    public async Task Replay(Guid cameraId,Guid completedSessionId,CancellationToken ct)
    {
        UseCase.LiveView(current);
        UseCase.Found(await store.Find<Camera>(cameraId,ct));
        await queries.EnsureSchema(ct);
        await replay.Replay(cameraId,completedSessionId,ct);
    }
}
