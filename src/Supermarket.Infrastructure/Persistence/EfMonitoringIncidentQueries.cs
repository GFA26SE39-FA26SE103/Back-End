using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Supermarket.Application;
using Supermarket.Infrastructure.Persistence.Scaffolded;
namespace Supermarket.Infrastructure.Persistence;

public sealed class EfMonitoringIncidentQueries(AppDbContext db):IMonitoringIncidentQueries
{
    public async Task EnsureSchema(CancellationToken ct)
    {
        var ready=await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN OBJECT_ID(N'dbo.Incident',N'U') IS NOT NULL AND OBJECT_ID(N'dbo.OperationalEvent',N'U') IS NOT NULL THEN 1 ELSE 0 END AS Value").SingleAsync(ct);
        if(ready!=1) throw new Supermarket.Application.ApplicationException("MONITORING_SCHEMA_NOT_READY","Run reviewed monitoring runtime migration 02 before using incident monitoring.",503);
    }
    public Task<DateTime?> LatestEndedAt(Guid zoneId,Guid typeId,CancellationToken ct)
        =>db.Incidents.Where(i=>i.ZoneId==zoneId && i.IncidentTypeId==typeId && i.ClosedAt!=null).MaxAsync(i=>i.ClosedAt,ct);

    public async Task<IncidentFeedView> PageForCamera(Guid cameraId,IncidentFeedQuery query,CancellationToken ct)
    {
        query.Validate(); await EnsureSchema(ct);
        var zones=db.CameraZoneMappings.Where(m=>m.CameraId==cameraId && m.Status=="ACTIVE").Select(m=>m.ZoneId);
        var rows=db.Incidents.AsNoTracking().Where(i=>i.TriggerCameraId==cameraId || zones.Contains(i.ZoneId));
        if(!query.IncludeEnded) rows=rows.Where(i=>i.ClosedAt==null);
        if(query.AfterCreatedAt is { } date && query.AfterIncidentId is { } id)
            rows=rows.Where(i=>i.CreatedAt<date || i.CreatedAt==date && i.IncidentId.CompareTo(id)<0);
        var page=await rows.OrderByDescending(i=>i.CreatedAt).ThenByDescending(i=>i.IncidentId).Take(query.Limit+1)
            .Select(i=>new { Row=i,ZoneName=i.Zone.Name,TypeCode=i.IncidentType.Code,TypeName=i.IncidentType.Name,
                CameraName=i.TriggerCamera==null?null:i.TriggerCamera.Name,
                Evidence=db.OperationalEvents.Where(e=>e.IncidentId==i.IncidentId).OrderByDescending(e=>e.DetectedAt).ThenByDescending(e=>e.EventId)
                    .Select(e=>new {e.MetricValue,e.MetadataJson}).FirstOrDefault() }).ToListAsync(ct);
        var hasMore=page.Count>query.Limit;
        var items=page.Take(query.Limit).Select(p=> {
            var mode=(string?)null; var unit=(string?)null; var source="UNKNOWN";
            if(p.Evidence?.MetadataJson is { } metadata)
                try {
                    using var document=JsonDocument.Parse(metadata); var root=document.RootElement;
                    if(root.ValueKind==JsonValueKind.Object) {
                        mode=Text(root,"measurementMode"); unit=Text(root,"thresholdUnit");
                        var value=Text(root,"videoSourceType"); if(value is "LIVE" or "RECORDED") source=value;
                    }
                } catch(JsonException) { }
            var i=p.Row;
            return new IncidentFeedItem(i.IncidentId,i.ZoneId,p.ZoneName,p.TypeCode,p.TypeName,i.TriggerCameraId,p.CameraName,i.Severity,i.Status,i.Title,
                Utc(i.CreatedAt),Utc(i.UpdatedAt),i.ClosedAt is null?null:Utc(i.ClosedAt.Value),p.Evidence?.MetricValue,mode,unit,source,source=="RECORDED");
        }).ToArray();
        return new(items,hasMore,hasMore?items[^1].CreatedAt:null,hasMore?items[^1].IncidentId:null);
    }
    private static string? Text(JsonElement root,string key)=>root.TryGetProperty(key,out var value) && value.ValueKind==JsonValueKind.String ? value.GetString():null;
    private static DateTime Utc(DateTime value)=>DateTime.SpecifyKind(value,DateTimeKind.Utc);
}
