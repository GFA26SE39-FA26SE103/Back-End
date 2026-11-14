using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Supermarket.Domain;
namespace Supermarket.Application;

public sealed class MonitoringSnapshotReader(ISetupStore store,CheckoutMonitoringOptions? checkoutOptions=null,
    ICheckoutSchema? checkoutSchema=null,ICheckoutConfigurationQueries? checkoutQueries=null):IMonitoringSnapshotReader
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private readonly CheckoutMonitoringOptions checkout=checkoutOptions??new();
    public async Task<MonitoringCameraSnapshot[]> ReadAll(CancellationToken ct)
    {
        var grouped=new Dictionary<Guid,(CameraConnection Connection,List<MonitoringZoneSnapshot> Zones,List<CheckoutMappingSnapshot> Checkouts,List<string> CheckoutVersions)>();
        var configurations=await store.List<MonitoringConfiguration>(c=>c.Status=="ACTIVE",ct);
        var checkoutZones=new HashSet<Guid>();
        foreach(var configuration in configurations)
        {
            var zone=UseCase.Found(await store.Find<Zone>(configuration.ZoneId,ct));
            var enabled=await store.List<MonitoringRule>(r=>r.ConfigId==configuration.ConfigId && r.Enabled,ct);
            if(enabled.Count>32) throw new ApplicationException("TOO_MANY_RULES","A configuration supports at most 32 rules.");
            var rules=new List<MonitoringRuleSnapshot>();
            var checkoutRules=new List<MonitoringRule>();
            var checkoutSnapshots=new List<MonitoringRuleSnapshot>();
            string? issue=zone.Status!="ACTIVE"?"ZONE_INACTIVE":enabled.Count==0?"NO_ENABLED_RULES":null;
            try { MonitoringPolicy.Confidence(configuration.ConfidenceThreshold); } catch(DomainException e) { issue??=e.Code; }
            foreach(var rule in enabled.OrderBy(r=>r.RuleId))
            {
                if(rule.OutcomeType=="OPERATIONAL_EVENT")
                {
                    var eventType=rule.EventTypeId is { } eventTypeId ? await store.Find<OperationalEventType>(eventTypeId,ct) : null;
                    if(eventType is null || eventType.Status!="ACTIVE") issue??="OPERATIONAL_EVENT_TYPE_UNAVAILABLE";
                    if(eventType?.Code=="EXCESSIVE_WAIT" && zone.ZoneType!="CHECKOUT") issue??="CHECKOUT_ZONE_REQUIRED";
                    if(eventType is not null) try { MonitoringPolicy.ValidateForActivation(rule,eventType); } catch(DomainException e) { issue??=e.Code; }
                    var definition=eventType is null ? new RuntimeMeasurementDefinition("UNSUPPORTED",rule.ThresholdUnit,false,"Operational event type unavailable.")
                        : MonitoringPolicy.RuntimeDefinition(rule,eventType);
                    var snapshot=new MonitoringRuleSnapshot(rule.RuleId,null,eventType?.Code??"UNKNOWN",eventType?.Name??"Unknown event type",definition.Mode,definition.Unit,
                        rule.WarningThreshold,rule.CriticalThreshold,rule.SustainSec,rule.CooldownSec,rule.EventTypeId,eventType?.Code??"",
                        rule.ClearDurationSec,rule.OutcomeType,rule.ParametersJson);
                    if(eventType?.Code=="CHECKOUT_CAPACITY" || eventType?.Code=="EXCESSIVE_WAIT" && zone.ZoneType=="CHECKOUT")
                    { checkoutRules.Add(rule); checkoutSnapshots.Add(snapshot); }
                    else rules.Add(snapshot);
                    continue;
                }
                var incidentType=rule.IncidentTypeId is { } incidentTypeId ? await store.Find<IncidentType>(incidentTypeId,ct) : null;
                issue??=incidentType is null?"INCIDENT_TYPE_UNAVAILABLE":"INCIDENT_RULE_RUNTIME_UNSUPPORTED";
                rules.Add(new(rule.RuleId,rule.IncidentTypeId,incidentType?.Code??"UNKNOWN",incidentType?.Name??"Unknown incident type","UNSUPPORTED",rule.ThresholdUnit,
                    rule.WarningThreshold,rule.CriticalThreshold,rule.SustainSec,rule.CooldownSec,null,"",rule.ClearDurationSec,rule.OutcomeType,rule.ParametersJson));
            }
            var mappings=rules.Count>0 || enabled.Count==0
                ? await store.List<CameraZoneMapping>(m=>m.ZoneId==zone.ZoneId && m.Status=="ACTIVE",ct) : [];
            if(rules.Count>0 && mappings.Select(m=>m.CameraId).Distinct().Count()!=1) issue??="MEASUREMENT_SOURCE_AMBIGUOUS";
            CheckoutSnapshotPreparation.Prepared? prepared=null;
            if(checkoutRules.Count>0)
            {
                checkoutZones.Add(zone.ZoneId);
                CheckoutSnapshotPreparation.Limit(checkoutZones.Count,checkout.MaxZones,128,"Active checkout zones");
                prepared=await CheckoutSnapshotPreparation.Read(store,checkout,zone,configuration,checkoutRules.ToArray(),checkoutSnapshots.ToArray(),checkoutSchema,checkoutQueries,ct);
                issue??=prepared.Issues.FirstOrDefault()?.Code;
            }
            foreach(var cameraMappings in mappings.GroupBy(m=>m.CameraId))
            {
                var camera=UseCase.Found(await store.Find<Camera>(cameraMappings.Key,ct));
                var connection=(await store.List<CameraConnection>(c=>c.CameraId==camera.CameraId,ct)).SingleOrDefault();
                var problem=issue; var rois=new List<Point[]>();
                try {
                    Rules.SameFloor(camera,zone);
                    if(cameraMappings.Count()>16) problem??="ROI_LIMIT_EXCEEDED";
                    foreach(var mapping in cameraMappings.OrderBy(m=>m.CameraZoneId)) {
                        var roi=JsonSerializer.Deserialize<Point[]>(mapping.RoiPolygon,Json)??[];
                        Rules.Polygon(roi); rois.Add(roi);
                    }
                }
                catch(JsonException) { problem??="ROI_INVALID"; } catch(DomainException e) { problem??=e.Code; }
                if(camera.Status!="ACTIVE") problem??="CAMERA_NOT_ACTIVE";
                if(connection is null) problem??="CONNECTION_MISSING";
                else {
                    try { Rules.Connection(connection); } catch(DomainException e) { problem??=e.Code; }
                    if(!connection.IsEnabled || connection.LastTestedAt is null || connection.LastTestResult!="SUCCESS") problem??="CONNECTION_NOT_READY";
                    if(!(connection.SourceType=="LIVE" && connection.Protocol is "HTTP" or "RTSP" or "HLS") && !(connection.SourceType=="RECORDED" && connection.Protocol=="FILE")) problem??="AI_SOURCE_UNSUPPORTED";
                }
                if(!grouped.TryGetValue(camera.CameraId,out var entry)) entry=(connection??new CameraConnection { CameraId=camera.CameraId },[],[],[]);
                entry.Zones.Add(new(zone.ZoneId,zone.Name,configuration.ConfigId,configuration.UpdatedAt,rois.FirstOrDefault()??[],configuration.ConfidenceThreshold,rules.ToArray(),problem,rois.Skip(1).ToArray()));
                grouped[camera.CameraId]=entry;
            }
            if(prepared is not null)
            {
                foreach(var cameraMappings in prepared.Mappings.GroupBy(m=>m.CameraId))
                {
                    if(!grouped.TryGetValue(cameraMappings.Key,out var entry))
                        entry=(prepared.Connections.GetValueOrDefault(cameraMappings.Key)??new CameraConnection { CameraId=cameraMappings.Key },[],[],[]);
                    entry.Checkouts.AddRange(cameraMappings.Select(m=>m with { IssueCode=issue??m.IssueCode }));
                    entry.CheckoutVersions.Add(prepared.Version);
                    grouped[cameraMappings.Key]=entry;
                }
            }
        }
        return grouped.OrderBy(g=>g.Key).Select(g=> {
            var connection=g.Value.Connection; var zones=g.Value.Zones.OrderBy(z=>z.ZoneId).ToArray();
            var checkouts=g.Value.Checkouts.OrderBy(m=>m.MappingId).ToArray();
            if(checkouts.Length>0) CheckoutSnapshotPreparation.Limit(checkouts.Length,checkout.MaxMappingsPerCamera,128,"Active checkout mappings per camera");
            var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
                cameraId=g.Key,connection.ConnectionId,connection.SourceType,connection.Protocol,connection.StreamUri,connection.Username,
                connection.CredentialSecretRef,connection.IsEnabled,connection.UpdatedAt,zones,checkouts,
                checkoutVersions=g.Value.CheckoutVersions.Order() },Json))));
            return new MonitoringCameraSnapshot(g.Key,connection,fingerprint,zones,
                zones.Select(z=>z.IssueCode).Concat(checkouts.Select(m=>m.IssueCode)).FirstOrDefault(i=>i is not null),checkouts);
        }).ToArray();
    }
    public async Task<bool> IsCurrent(MonitoringCameraSnapshot snapshot,Guid zoneId,CancellationToken ct)
    {
        var current=(await ReadAll(ct)).SingleOrDefault(c=>c.CameraId==snapshot.CameraId);
        return current is not null && current.IssueCode is null && current.Fingerprint==snapshot.Fingerprint
            && (current.Zones.Any(z=>z.ZoneId==zoneId) || current.CheckoutMappings?.Any(m=>m.ZoneId==zoneId)==true);
    }
    public async Task<bool> IsCurrentAggregate(MonitoringCameraSnapshot[] snapshots,Guid zoneId,CancellationToken ct)
    {
        if(snapshots.Length==0 || snapshots.Length>128*16
            || snapshots.Select(s=>s.CameraId).Distinct().Count()!=snapshots.Length) return false;
        // The caller holds the writer transaction. Re-read the whole source set once, not once per camera.
        var current=(await ReadAll(ct)).Where(s=>s.CheckoutMappings?.Any(m=>m.ZoneId==zoneId)==true).ToDictionary(s=>s.CameraId);
        if(current.Count!=snapshots.Length) return false;
        foreach(var expected in snapshots)
        {
            if(expected.IssueCode is not null || expected.CheckoutMappings?.Any(m=>m.ZoneId==zoneId)!=true
                || !current.TryGetValue(expected.CameraId,out var source) || source.IssueCode is not null
                || source.Fingerprint!=expected.Fingerprint) return false;
        }
        static IEnumerable<(Guid CameraId,Guid MappingId,Guid CounterId)> Sources(IEnumerable<MonitoringCameraSnapshot> cameras,Guid zone)
            => cameras.SelectMany(c=>(c.CheckoutMappings??[]).Where(m=>m.ZoneId==zone)
                .Select(m=>(c.CameraId,m.MappingId,m.CounterId)));
        return Sources(snapshots,zoneId).ToHashSet().SetEquals(Sources(current.Values,zoneId));
    }
}
