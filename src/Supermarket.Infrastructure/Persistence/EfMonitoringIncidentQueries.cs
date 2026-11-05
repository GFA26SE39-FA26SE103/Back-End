using Microsoft.EntityFrameworkCore;
using Supermarket.Application;
using Supermarket.Infrastructure.Persistence.Scaffolded;
using Task = System.Threading.Tasks.Task;
namespace Supermarket.Infrastructure.Persistence;

public sealed class EfMonitoringIncidentQueries(AppDbContext db):IMonitoringIncidentQueries
{
    public async Task EnsureSchema(CancellationToken ct)
    {
        var ready=await db.Database.SqlQueryRaw<int>("""
            SELECT CASE WHEN OBJECT_ID(N'dbo.Incident',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.OperationalEvent',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.OperationalEventType',N'U') IS NOT NULL
                AND COL_LENGTH(N'dbo.MonitoringRule',N'event_type_id') IS NOT NULL
                AND COL_LENGTH(N'dbo.MonitoringRule',N'outcome_type') IS NOT NULL
                AND COL_LENGTH(N'dbo.Incident',N'monitoring_rule_id') IS NOT NULL
                AND COL_LENGTH(N'dbo.Incident',N'severity') IS NULL
                AND COL_LENGTH(N'dbo.IncidentType',N'default_severity') IS NULL
                AND COL_LENGTH(N'dbo.Incident',N'sla_policy_id') IS NOT NULL
                AND COL_LENGTH(N'dbo.Incident',N'sla_snapshot_json') IS NOT NULL
                AND COL_LENGTH(N'dbo.Incident',N'resolution_deadline_at') IS NOT NULL
                AND COL_LENGTH(N'dbo.MonitoringRule',N'clear_duration_sec') IS NOT NULL
                AND OBJECT_ID(N'dbo.SLAPolicy',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.StaffSpecialty',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.TaskType',N'U') IS NOT NULL
                AND OBJECT_ID(N'dbo.TaskOffer',N'U') IS NOT NULL
                AND COL_LENGTH(N'dbo.OperationalEvent',N'event_type_id') IS NOT NULL
                AND COL_LENGTH(N'dbo.OperationalEvent',N'cleared_at') IS NOT NULL
                AND COL_LENGTH(N'dbo.OperationalEvent',N'incident_id') IS NULL
                AND COL_LENGTH(N'dbo.OperationalEvent',N'event_type') IS NULL
                AND COL_LENGTH(N'dbo.Incident',N'trigger_camera_id') IS NULL
                AND COL_LENGTH(N'dbo.IncidentType',N'source_type') IS NULL
                AND COL_LENGTH(N'dbo.IncidentType',N'measurement_type') IS NULL
                AND OBJECT_ID(N'dbo.NotificationConfig',N'U') IS NULL
                AND OBJECT_ID(N'dbo.ZoneShiftRequirement',N'U') IS NULL
                AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MonitoringRule')
                    AND name=N'UX_MonitoringRule_Config_EventType')
                AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.MonitoringRule')
                    AND name=N'UX_MonitoringRule_Config_IncidentType')
                AND EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.OperationalEvent') AND name=N'UX_OperationalEvent_Active_Rule_Zone')
                THEN 1 ELSE 0 END AS Value
            """).SingleAsync(ct);
        if(ready!=1) throw new Supermarket.Application.ApplicationException("MONITORING_SCHEMA_NOT_READY","Apply the approved ERD V7 Database V9 migration before using operational monitoring.",503);
    }
    public Task<DateTime?> LatestEndedAt(Guid zoneId,Guid typeId,CancellationToken ct)
        =>db.Incidents.Where(i=>i.ZoneId==zoneId && i.IncidentTypeId==typeId && i.ClosedAt!=null).MaxAsync(i=>i.ClosedAt,ct);

    public async Task<IncidentFeedView> PageForCamera(Guid cameraId,IncidentFeedQuery query,CancellationToken ct)
    {
        query.Validate(); await EnsureSchema(ct);
        var zones=db.CameraZoneMappings.Where(m=>m.CameraId==cameraId && m.Status=="ACTIVE").Select(m=>m.ZoneId);
        var rows=db.Incidents.AsNoTracking().Where(i=>zones.Contains(i.ZoneId));
        if(!query.IncludeEnded) rows=rows.Where(i=>i.ClosedAt==null);
        if(query.AfterCreatedAt is { } date && query.AfterIncidentId is { } id)
            rows=rows.Where(i=>i.CreatedAt<date || i.CreatedAt==date && i.IncidentId.CompareTo(id)<0);
        var page=await rows.OrderByDescending(i=>i.CreatedAt).ThenByDescending(i=>i.IncidentId).Take(query.Limit+1)
            .Select(i=>new { Row=i,ZoneName=i.Zone.Name,TypeCode=i.IncidentType.Code,TypeName=i.IncidentType.Name }).ToListAsync(ct);
        var hasMore=page.Count>query.Limit;
        var items=page.Take(query.Limit).Select(p=>
        {
            var snapshot=ReadSnapshot(p.Row.SlaSnapshotJson);
            return new IncidentFeedItem(p.Row.IncidentId,p.Row.ZoneId,p.ZoneName,p.TypeCode,p.TypeName,null,null,
                p.Row.Status,p.Row.Title,Utc(p.Row.CreatedAt),Utc(p.Row.UpdatedAt),p.Row.ClosedAt is null?null:Utc(p.Row.ClosedAt.Value),
                p.Row.SlaPolicyId,snapshot?.PriorityCode,snapshot?.PolicyVersion,snapshot?.ResponseDeadlineAt,
                p.Row.ResolutionDeadlineAt is null?null:Utc(p.Row.ResolutionDeadlineAt.Value),p.Row.MitigatedAt is null?null:Utc(p.Row.MitigatedAt.Value),
                null,null,null,"UNKNOWN",false);
        }).ToArray();
        return new(items,hasMore,hasMore?items[^1].CreatedAt:null,hasMore?items[^1].IncidentId:null);
    }
    private static DateTime Utc(DateTime value)=>DateTime.SpecifyKind(value,DateTimeKind.Utc);
    private static IncidentSlaSnapshot? ReadSnapshot(string? json)
    {
        if(string.IsNullOrWhiteSpace(json)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<IncidentSlaSnapshot>(json,IncidentSlaPolicyService.JsonOptions); }
        catch(System.Text.Json.JsonException) { return null; }
    }
}
