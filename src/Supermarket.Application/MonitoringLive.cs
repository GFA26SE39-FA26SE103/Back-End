using Supermarket.Domain;
namespace Supermarket.Application;

public sealed class MonitoringLive(ISetupStore store,ICurrentUser current,IMonitoringRuntimeState runtime,IMonitoringIncidentQueries queries,IMonitoringSnapshotReader snapshots)
{
    public async Task<MonitoringCameraRuntimeView> Runtime(Guid cameraId,CancellationToken ct)
    {
        UseCase.LiveView(current); UseCase.Found(await store.Find<Camera>(cameraId,ct)); await queries.EnsureSchema(ct);
        var cached=runtime.Get(cameraId);
        var zones=new List<MonitoringZoneRuntimeView>();
        foreach(var mapping in await store.List<CameraZoneMapping>(m=>m.CameraId==cameraId && m.Status=="ACTIVE",ct))
        {
            var zone=UseCase.Found(await store.Find<Zone>(mapping.ZoneId,ct));
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
                        var definition=type is null ? new RuntimeMeasurementDefinition("UNSUPPORTED",rule.ThresholdUnit,false,"Operational event type unavailable.") : MonitoringPolicy.RuntimeDefinition(rule,type);
                        ruleViews.Add(new(rule.RuleId,type?.Code??"UNKNOWN",type?.Name??"Unknown event type",definition.Mode,definition.Unit,rule.WarningThreshold,rule.CriticalThreshold,rule.SustainSec,rule.CooldownSec,null,0,0,0,
                            config.Status!="ACTIVE"?"NO_ACTIVE_CONFIGURATION":!definition.Supported?"RULE_RUNTIME_UNSUPPORTED":"STARTING"));
                    }
                    else
                    {
                        var type=rule.IncidentTypeId is { } incidentTypeId ? await store.Find<IncidentType>(incidentTypeId,ct) : null;
                        ruleViews.Add(new(rule.RuleId,type?.Code??"UNKNOWN",type?.Name??"Unknown incident type","UNSUPPORTED",rule.ThresholdUnit,rule.WarningThreshold,rule.CriticalThreshold,
                            rule.SustainSec,rule.CooldownSec,null,0,0,0,"INCIDENT_RULE_RUNTIME_UNSUPPORTED"));
                    }
                }
            zones.Add(new(zone.ZoneId,zone.Name,config?.ConfigId,config?.UpdatedAt,config?.Status??"NOT_CONFIGURED",config?.ConfidenceThreshold??0,null,null,ruleViews.ToArray()));
        }
        var active=zones.Where(z=>z.ConfigurationStatus=="ACTIVE").ToArray();
        var source=(await store.List<CameraConnection>(c=>c.CameraId==cameraId,ct)).SingleOrDefault()?.SourceType;
        if(active.Length==0)
        {
            // Desired SQL state can be inactive before the next worker tick has
            // released its owner. Do not advertise replay readiness too early.
            var stopping=cached?.Zones.Any(z=>z.ConfigurationStatus=="ACTIVE")==true;
            return new(cameraId,stopping?"STOPPING":"STOPPED",stopping?"MONITORING_STOP_PENDING":"NO_ACTIVE_CONFIGURATION",null,
                source,null,null,null,false,null,zones.ToArray());
        }
        if(cached is not null && active.All(z=>cached.Zones.Any(c=>c.ConfigId==z.ConfigId && c.ConfigVersion==z.ConfigVersion)))
            return cached with { Zones=zones.ToArray() };
        var snapshot=(await snapshots.ReadAll(ct)).SingleOrDefault(s=>s.CameraId==cameraId);
        if(snapshot?.IssueCode is { } issue) return new(cameraId,"ERROR",issue,issue,source,null,null,null,true,null,zones.ToArray());
        return new(cameraId,"STARTING","STARTING",null,source,null,null,null,cached is not null,null,zones.ToArray());
    }
    public async Task<IncidentFeedView> Incidents(Guid cameraId,IncidentFeedQuery query,CancellationToken ct)
    {
        UseCase.LiveView(current); UseCase.Found(await store.Find<Camera>(cameraId,ct));
        return await queries.PageForCamera(cameraId,query,ct);
    }
}
