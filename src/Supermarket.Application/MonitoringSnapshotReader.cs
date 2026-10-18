using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Supermarket.Domain;
namespace Supermarket.Application;

public sealed class MonitoringSnapshotReader(ISetupStore store):IMonitoringSnapshotReader
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public async Task<MonitoringCameraSnapshot[]> ReadAll(CancellationToken ct)
    {
        var grouped=new Dictionary<Guid,(CameraConnection Connection,List<MonitoringZoneSnapshot> Zones)>();
        foreach(var configuration in await store.List<MonitoringConfiguration>(c=>c.Status=="ACTIVE",ct))
        {
            var zone=UseCase.Found(await store.Find<Zone>(configuration.ZoneId,ct));
            var mappings=await store.List<CameraZoneMapping>(m=>m.ZoneId==zone.ZoneId && m.Status=="ACTIVE",ct);
            var enabled=await store.List<MonitoringRule>(r=>r.ConfigId==configuration.ConfigId && r.Enabled,ct);
            var rules=new List<MonitoringRuleSnapshot>();
            string? issue=zone.Status!="ACTIVE"?"ZONE_INACTIVE":mappings.Count!=1?"MEASUREMENT_SOURCE_AMBIGUOUS":enabled.Count==0?"NO_ENABLED_RULES":null;
            try { MonitoringPolicy.Confidence(configuration.ConfidenceThreshold); } catch(DomainException e) { issue??=e.Code; }
            foreach(var rule in enabled.OrderBy(r=>r.RuleId))
            {
                if(rule.OutcomeType=="OPERATIONAL_EVENT")
                {
                    var eventType=rule.EventTypeId is { } eventTypeId ? await store.Find<OperationalEventType>(eventTypeId,ct) : null;
                    if(eventType is null || eventType.Status!="ACTIVE") issue??="OPERATIONAL_EVENT_TYPE_UNAVAILABLE";
                    if(eventType is not null) try { MonitoringPolicy.ValidateForActivation(rule,eventType); } catch(DomainException e) { issue??=e.Code; }
                    var definition=eventType is null ? new RuntimeMeasurementDefinition("UNSUPPORTED",rule.ThresholdUnit,false,"Operational event type unavailable.")
                        : MonitoringPolicy.RuntimeDefinition(rule,eventType);
                    rules.Add(new(rule.RuleId,null,eventType?.Code??"UNKNOWN",eventType?.Name??"Unknown event type",definition.Mode,definition.Unit,
                        rule.WarningThreshold,rule.CriticalThreshold,rule.SustainSec,rule.CooldownSec,rule.EventTypeId,eventType?.Code??"",
                        MonitoringPolicy.RecoverySeconds(rule),rule.OutcomeType));
                    continue;
                }
                var incidentType=rule.IncidentTypeId is { } incidentTypeId ? await store.Find<IncidentType>(incidentTypeId,ct) : null;
                issue??=incidentType is null?"INCIDENT_TYPE_UNAVAILABLE":"INCIDENT_RULE_RUNTIME_UNSUPPORTED";
                rules.Add(new(rule.RuleId,rule.IncidentTypeId,incidentType?.Code??"UNKNOWN",incidentType?.Name??"Unknown incident type","UNSUPPORTED",rule.ThresholdUnit,
                    rule.WarningThreshold,rule.CriticalThreshold,rule.SustainSec,rule.CooldownSec,null,"",MonitoringPolicy.RecoverySeconds(rule),rule.OutcomeType));
            }
            foreach(var mapping in mappings)
            {
                var camera=UseCase.Found(await store.Find<Camera>(mapping.CameraId,ct));
                var connection=(await store.List<CameraConnection>(c=>c.CameraId==camera.CameraId,ct)).SingleOrDefault();
                var problem=issue; Point[] roi=[];
                try { Rules.SameFloor(camera,zone); roi=JsonSerializer.Deserialize<Point[]>(mapping.RoiPolygon,Json)??[]; Rules.Polygon(roi); }
                catch(JsonException) { problem??="ROI_INVALID"; } catch(DomainException e) { problem??=e.Code; }
                if(camera.Status!="ACTIVE") problem??="CAMERA_NOT_ACTIVE";
                if(connection is null) problem??="CONNECTION_MISSING";
                else {
                    try { Rules.Connection(connection); } catch(DomainException e) { problem??=e.Code; }
                    if(!connection.IsEnabled || connection.LastTestedAt is null || connection.LastTestResult!="SUCCESS") problem??="CONNECTION_NOT_READY";
                    if(!(connection.SourceType=="LIVE" && connection.Protocol is "HTTP" or "RTSP" or "HLS") && !(connection.SourceType=="RECORDED" && connection.Protocol=="FILE")) problem??="AI_SOURCE_UNSUPPORTED";
                }
                if(!grouped.TryGetValue(camera.CameraId,out var entry)) entry=(connection??new CameraConnection { CameraId=camera.CameraId },[]);
                entry.Zones.Add(new(zone.ZoneId,zone.Name,configuration.ConfigId,configuration.UpdatedAt,roi,configuration.ConfidenceThreshold,rules.ToArray(),problem));
                grouped[camera.CameraId]=entry;
            }
        }
        return grouped.OrderBy(g=>g.Key).Select(g=> {
            var connection=g.Value.Connection; var zones=g.Value.Zones.OrderBy(z=>z.ZoneId).ToArray();
            var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
                cameraId=g.Key,connection.ConnectionId,connection.SourceType,connection.Protocol,connection.StreamUri,connection.Username,
                connection.CredentialSecretRef,connection.IsEnabled,connection.UpdatedAt,zones },Json))));
            return new MonitoringCameraSnapshot(g.Key,connection,fingerprint,zones,zones.Select(z=>z.IssueCode).FirstOrDefault(i=>i is not null));
        }).ToArray();
    }
    public async Task<bool> IsCurrent(MonitoringCameraSnapshot snapshot,Guid zoneId,CancellationToken ct)
    {
        var current=(await ReadAll(ct)).SingleOrDefault(c=>c.CameraId==snapshot.CameraId);
        return current is not null && current.IssueCode is null && current.Fingerprint==snapshot.Fingerprint && current.Zones.Any(z=>z.ZoneId==zoneId);
    }
}
