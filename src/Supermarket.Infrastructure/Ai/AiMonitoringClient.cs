using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using AppError=Supermarket.Application.ApplicationException;
namespace Supermarket.Infrastructure.Ai;

public sealed class AiMonitoringClient(HttpClient http,IOptions<AiPreviewOptions> configured,ICredentialProtector secrets):IAiMonitoringClient
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web) { PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower };
    private static readonly HashSet<string> Codes=["AI_SESSION_CAPACITY","AI_SESSION_OWNER_MISMATCH","AI_SESSION_MONITORING_OWNED","AI_SERVICE_UNAUTHORIZED","AI_REQUEST_INVALID","AI_PREVIEW_NOT_RUNNING","AI_PREVIEW_FAILED","AI_SOURCE_TIME_INVALID","AI_RECORDED_VIDEO_INVALID","CAMERA_STREAM_UNAVAILABLE"];
    private readonly AiPreviewOptions options=configured.Value;
    public Task<AiPreviewStatusView> Start(MonitoringCameraSnapshot snapshot,Guid ownerId,CancellationToken ct)
    {
        var connection=snapshot.Connection;
        var body=new { protocol_version=1,owner_id=ownerId,configuration_fingerprint=snapshot.Fingerprint,
            stream_url=connection.StreamUri,source_type=connection.SourceType,username=connection.Username,
            password=connection.CredentialSecretRef is null?null:secrets.Unprotect(connection.CredentialSecretRef),
            model=options.Model,tracker=options.Tracker,classes=options.Classes,device=options.Device,half=options.Half,
            confidence=snapshot.Zones.Min(z=>z.Confidence),zones=snapshot.Zones.Select(z=>new { zone_id=z.ZoneId,config_id=z.ConfigId,config_version=z.ConfigVersion,
                roi_polygon=z.Roi,confidence=z.Confidence,queue_enabled=z.Rules.Any(r=>r.Mode=="QUEUE_LENGTH") }).ToArray() };
        return Send<AiPreviewStatusView>(HttpMethod.Post,$"/monitoring/sessions/{snapshot.CameraId}/start",body,null,ct);
    }
    public async Task<AiMeasurementPage> Read(Guid cameraId,Guid ownerId,Guid? afterSessionId,long afterSequence,int limit,CancellationToken ct)
    {
        if(limit is <1 or >64 || afterSequence<0) throw Invalid();
        var query=$"after_sequence={afterSequence}&limit={limit}"+(afterSessionId is null?"":$"&after_session_id={afterSessionId}");
        var page=await Send<AiMeasurementPage>(HttpMethod.Get,$"/monitoring/sessions/{cameraId}/measurements?{query}",null,ownerId,ct);
        if(page.ProtocolVersion!=1 || page.SessionId==Guid.Empty || page.Batches is null || page.Batches.Length>limit
            || page.NewestSequence<0 || page.OldestSequence<1 || page.OldestSequence-1>page.NewestSequence || page.State is not ("STARTING" or "LIVE" or "RECONNECTING" or "ERROR" or "COMPLETED" or "STOPPED")) throw Invalid();
        long previous=0;
        foreach(var batch in page.Batches)
        {
            if(batch.ProtocolVersion!=1 || batch.SessionId!=page.SessionId || batch.ContinuityId==Guid.Empty || batch.Sequence<=previous
               || batch.Sequence<page.OldestSequence || batch.Sequence>page.NewestSequence || batch.SourceElapsedMs<0 || batch.CapturedAt.Kind!=DateTimeKind.Utc
               || string.IsNullOrWhiteSpace(batch.ConfigurationFingerprint) || batch.Zones is null || batch.Zones.Length>128
               || batch.Zones.Select(z=>z.ZoneId).Distinct().Count()!=batch.Zones.Length
               || batch.Zones.Any(z=>z.ZoneId==Guid.Empty || z.ConfigId==Guid.Empty || z.ConfigVersion.Kind!=DateTimeKind.Utc || z.PeopleCount<0 || z.QueueCount<0 || z.QueueCount>z.PeopleCount)) throw Invalid();
            previous=batch.Sequence;
        }
        return page with { ErrorCode=page.ErrorCode is null?null:Codes.Contains(page.ErrorCode)?page.ErrorCode:"AI_MEASUREMENT_INVALID" };
    }
    public async Task Stop(Guid cameraId,Guid ownerId,CancellationToken ct)
        => _=await Send<AiPreviewStatusView>(HttpMethod.Delete,$"/monitoring/sessions/{cameraId}",null,ownerId,ct);

    private async Task<T> Send<T>(HttpMethod method,string path,object? body,Guid? owner,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(method,path);
        if(body is not null) request.Content=JsonContent.Create(body,options:Json);
        if(owner is not null) request.Headers.TryAddWithoutValidation("X-AI-Monitoring-Owner",owner.ToString());
        if(!string.IsNullOrEmpty(options.InternalServiceKey)) request.Headers.TryAddWithoutValidation("X-AI-Service-Key",options.InternalServiceKey);
        try
        {
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
            if(!response.IsSuccessStatusCode)
            {
                var code="AI_SERVICE_UNAVAILABLE";
                try { using var error=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    if(error.RootElement.TryGetProperty("code",out var supplied) && supplied.ValueKind==JsonValueKind.String && Codes.Contains(supplied.GetString()!)) code=supplied.GetString()!;
                } catch(JsonException) { }
                throw new AppError(code,"AI monitoring is unavailable or the session cannot be used.",response.StatusCode==System.Net.HttpStatusCode.Conflict?409:503);
            }
            return await response.Content.ReadFromJsonAsync<T>(Json,ct) ?? throw Invalid();
        }
        catch(JsonException) { throw Invalid(); }
        catch(HttpRequestException) { throw new AppError("AI_SERVICE_UNAVAILABLE","AI monitoring service is unavailable.",503); }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested) { throw new AppError("AI_SERVICE_UNAVAILABLE","AI monitoring service timed out.",503); }
    }
    private static AppError Invalid()=>new("AI_MEASUREMENT_INVALID","AI monitoring returned an invalid measurement response.",503);
}
