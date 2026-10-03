using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Domain;
using AppError=Supermarket.Application.ApplicationException;
namespace Supermarket.Infrastructure.Monitoring;

public sealed class MonitoringCameraCoordinator(IServiceScopeFactory scopes,IOptions<MonitoringWorkerOptions> configured,IClock clock):IMonitoringRuntimeState,IMonitoringSessionOwnership
{
    private readonly MonitoringWorkerOptions options=configured.Value;
    private readonly Dictionary<Guid,Job> jobs=[];
    private readonly ConcurrentDictionary<Guid,MonitoringCameraRuntimeView> views=new();
    private readonly SemaphoreSlim gate=new(1,1);
    public MonitoringCameraRuntimeView? Get(Guid cameraId)=>views.GetValueOrDefault(cameraId);
    public bool IsMonitoringOwned(Guid cameraId)=>views.TryGetValue(cameraId,out var view) && view.Zones.Any(z=>z.ConfigurationStatus=="ACTIVE");
    public async Task Tick(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            using var scope=scopes.CreateScope(); var services=scope.ServiceProvider;
            MonitoringCameraSnapshot[] snapshots;
            try { snapshots=await services.GetRequiredService<IMonitoringSnapshotReader>().ReadAll(ct); }
            catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
            catch(Exception error) { foreach(var job in jobs.Values) Error(job,Code(error)); return; }
            var ai=services.GetRequiredService<IAiMonitoringClient>();
            foreach(var id in jobs.Keys.Where(id=>!snapshots.Any(s=>s.CameraId==id)).ToArray())
            {
                var removed=jobs[id];
                if(removed.Started) try { await ai.Stop(id,removed.OwnerId,ct); } catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; } catch(Exception) { /* Lease expires if service is down. */ }
                jobs.Remove(id); views.TryRemove(id,out _);
            }
            foreach(var snapshot in snapshots)
            {
                if(!jobs.TryGetValue(snapshot.CameraId,out var job)) { job=new(snapshot); jobs.Add(snapshot.CameraId,job); }
                var changed=job.Snapshot.Fingerprint!=snapshot.Fingerprint;
                if(changed) { job.Snapshot=snapshot; job.RuleStates.Clear(); job.Samples.Clear(); job.Measurements.Clear(); job.RuleViews.Clear(); job.RetryAt=DateTime.MinValue; job.SourceMs=null; job.ObservedAt=null; }
                if(snapshot.IssueCode is { } issue)
                {
                    if(job.Started) try { await ai.Stop(snapshot.CameraId,job.OwnerId,ct); job.Started=false; } catch(Exception) when(!ct.IsCancellationRequested) { }
                    Error(job,issue); continue;
                }
                if(clock.UtcNow<job.RetryAt) continue;
                try
                {
                    await services.GetRequiredService<IMonitoringIncidentQueries>().EnsureSchema(ct);
                    var source=Source(snapshot.Connection);
                    if(job.Started && job.AppliedSource!=source)
                    {
                        // A changed source/credential is a new reader, not an ROI-only
                        // reconfigure. Release the owner first; never retry forever on
                        // AI_SESSION_MONITORING_OWNED after a rapid edit/reactivate.
                        await ai.Stop(snapshot.CameraId,job.OwnerId,ct);
                        job.Started=false; job.AppliedFingerprint=null; job.SessionId=null; job.Sequence=0;
                    }
                    if(!job.Started || job.AppliedFingerprint!=snapshot.Fingerprint)
                    {
                        Publish(job,"STARTING","STARTING");
                        var started=await ai.Start(snapshot,job.OwnerId,ct);
                        job.Started=true; job.AppliedFingerprint=snapshot.Fingerprint; job.AppliedSource=source; job.AnnotationContext=started.AnnotationContext;
                        if(job.SessionId!=started.SessionId) { job.SessionId=started.SessionId; job.Sequence=0; job.RuleStates.Clear(); job.Samples.Clear(); }
                    }
                    var page=await ai.Read(snapshot.CameraId,job.OwnerId,job.SessionId,job.Sequence,Math.Clamp(options.MaxBatch,1,64),ct);
                    if(page.SessionId!=job.SessionId || page.Gap)
                    {
                        job.SessionId=page.SessionId; job.Sequence=Math.Max(0,page.OldestSequence-1);
                        job.RuleStates.Clear(); job.Samples.Clear(); job.SourceMs=null;
                    }
                    foreach(var batch in page.Batches)
                    {
                        if(batch.Sequence<=job.Sequence) continue;
                        if(batch.ConfigurationFingerprint!=snapshot.Fingerprint) { job.Sequence=batch.Sequence; continue; }
                        var gap=job.SourceMs is { } ms && (batch.SourceElapsedMs<ms || snapshot.Connection.SourceType=="LIVE" && batch.SourceElapsedMs-ms>options.MaxObservationGapMs);
                        foreach(var zone in snapshot.Zones)
                        {
                            var measurement=batch.Zones.SingleOrDefault(z=>z.ZoneId==zone.ZoneId && z.ConfigId==zone.ConfigId && z.ConfigVersion==zone.ConfigVersion)
                                ?? throw new AppError("AI_MEASUREMENT_INVALID","AI returned an incomplete configuration measurement.",503);
                            foreach(var rule in zone.Rules)
                            {
                                var state=job.RuleStates.GetValueOrDefault(rule.RuleId)??new RuleEvaluationState();
                                // A partially committed frame is replayed at the global cursor.
                                // Preserve successful rules and retry only the failed rule.
                                if(state.SessionId==batch.SessionId && state.ConfigFingerprint==snapshot.Fingerprint && state.Sequence>=batch.Sequence) continue;
                                var metric=rule.Mode=="QUEUE_LENGTH"?measurement.QueueCount:measurement.PeopleCount;
                                var evaluation=MonitoringRuleEvaluator.Evaluate(rule.Settings(),state,new(batch.SessionId,batch.ContinuityId,batch.Sequence,batch.SourceElapsedMs,metric,snapshot.Fingerprint,gap));
                                var second=batch.SourceElapsedMs/1000;
                                var sample=!job.Samples.TryGetValue(rule.RuleId,out var sampled) || sampled!=second;
                                var continuous=!gap && state.SessionId==batch.SessionId && state.ContinuityId==batch.ContinuityId
                                    && state.ConfigFingerprint==snapshot.Fingerprint && state.Sequence+1==batch.Sequence
                                    && batch.SourceElapsedMs>=state.SourceElapsedMs;
                                var sustain=rule.SustainSec*1000L;
                                var priorSeverity=!continuous?null:state.CriticalStartMs is { } critical && state.SourceElapsedMs-critical>=sustain?"CRITICAL"
                                    :state.WarningStartMs is { } warning && state.SourceElapsedMs-warning>=sustain?"WARNING":null;
                                var milestone=evaluation.EligibleSeverity is not null && evaluation.EligibleSeverity!=priorSeverity;
                                var prior=job.RuleViews.GetValueOrDefault(rule.RuleId);
                                // Evaluate every ordered frame, but query SQL only for a source-second
                                // sample or a newly sustained severity. Keep the last observed open
                                // incident between samples; a lower count never closes it.
                                var written=sample || milestone
                                    ?await services.GetRequiredService<MonitoringIncidentWriter>().Apply(new(snapshot,zone,rule,batch,measurement,evaluation,sample),ct)
                                    :new IncidentWriteResult(evaluation.Accepted,false,false,prior?.IncidentId,prior?.IncidentSeverity,evaluation.Reason,prior?.CooldownRemainingSec??0);
                                if(written.Stale) { job.RuleStates.Remove(rule.RuleId); continue; }
                                job.RuleStates[rule.RuleId]=evaluation.NextState;
                                if(sample && written.Applied) job.Samples[rule.RuleId]=second;
                                var reason=written.CooldownRemainingSec>0?"COOLDOWN":written.Reason;
                                if(rule.Mode=="QUEUE_LENGTH" && measurement.PeopleCount>0 && measurement.QueueCount==0 && reason is "NORMAL" or "SUSTAIN_PENDING") reason="QUEUE_DWELL_PENDING";
                                job.RuleViews[rule.RuleId]=new(rule.RuleId,rule.Code,rule.Name,rule.Mode,rule.Unit,rule.WarningThreshold,rule.CriticalThreshold,rule.SustainSec,rule.CooldownSec,metric,
                                    evaluation.WarningProgressMs,evaluation.CriticalProgressMs,written.CooldownRemainingSec,reason,written.IncidentId,written.Severity);
                            }
                            job.Measurements[zone.ZoneId]=measurement;
                        }
                        job.Sequence=batch.Sequence; job.SourceMs=batch.SourceElapsedMs; job.ObservedAt=batch.CapturedAt;
                    }
                    if(page.State is "ERROR" or "RECONNECTING" or "STOPPED")
                    {
                        Error(job,page.ErrorCode??(page.State=="STOPPED"?"AI_PREVIEW_NOT_RUNNING":"CAMERA_STREAM_UNAVAILABLE"));
                        job.Started=page.State=="RECONNECTING"; continue;
                    }
                    job.Failures=0; job.RetryAt=DateTime.MinValue;
                    var stateName=page.State=="COMPLETED" && job.Sequence>=page.NewestSequence?"COMPLETED":job.ObservedAt is null?"STARTING":"RUNNING";
                    Publish(job,stateName,stateName=="RUNNING"?"MEASUREMENTS_AVAILABLE":stateName);
                }
                catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
                catch(Exception error)
                {
                    var code=Code(error);
                    // SQL retry keeps committed per-rule state and the frame cursor.
                    // Transport session loss requests a new owner-controlled start.
                    if(code is "AI_PREVIEW_NOT_RUNNING" or "AI_SESSION_OWNER_MISMATCH") job.Started=false;
                    Error(job,code,resetContinuity:false);
                }
            }
        }
        finally { gate.Release(); }
    }
    private void Error(Job job,string code,bool resetContinuity=true)
    {
        if(resetContinuity) job.RuleStates.Clear();
        job.Failures++;
        job.RetryAt=clock.UtcNow.AddSeconds(Math.Min(30,Math.Pow(2,Math.Min(5,job.Failures-1))));
        Publish(job,"ERROR",code,code,true);
    }
    private void Publish(Job job,string state,string reason,string? code=null,bool stale=false)
    {
        var zones=job.Snapshot.Zones.Select(z=> {
            var measured=job.Measurements.GetValueOrDefault(z.ZoneId);
            var rules=z.Rules.Select(r=>job.RuleViews.GetValueOrDefault(r.RuleId)??new MonitoringRuleRuntimeView(r.RuleId,r.Code,r.Name,r.Mode,r.Unit,r.WarningThreshold,r.CriticalThreshold,r.SustainSec,r.CooldownSec,null,0,0,0,code??state)).ToArray();
            return new MonitoringZoneRuntimeView(z.ZoneId,z.ZoneName,z.ConfigId,z.ConfigVersion,"ACTIVE",z.Confidence,measured?.PeopleCount,measured?.QueueCount,rules,z.IssueCode);
        }).ToArray();
        views[job.Snapshot.CameraId]=new(job.Snapshot.CameraId,state,reason,code,job.Snapshot.Connection.SourceType,job.SessionId,job.SourceMs,job.ObservedAt,stale,job.AnnotationContext,zones);
    }
    public async Task Shutdown(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try {
            if(jobs.Count==0) return; // Also permits the host's repeated stop/dispose.
            using var scope=scopes.CreateScope(); var ai=scope.ServiceProvider.GetRequiredService<IAiMonitoringClient>();
            foreach(var job in jobs.Values.Where(j=>j.Started)) try { await ai.Stop(job.Snapshot.CameraId,job.OwnerId,ct); } catch(Exception) when(!ct.IsCancellationRequested) { }
            jobs.Clear(); views.Clear();
        } finally { gate.Release(); }
    }
    private static string Code(Exception error)=>error switch { AppError e=>e.Code,DomainException e=>e.Code,_=>"MONITORING_UNAVAILABLE" };
    private static (string,string,string,string?,string?) Source(CameraConnection c)=>(c.SourceType,c.Protocol,c.StreamUri,c.Username,c.CredentialSecretRef);
    private sealed class Job(MonitoringCameraSnapshot snapshot)
    {
        public MonitoringCameraSnapshot Snapshot=snapshot;
        public readonly Guid OwnerId=Guid.NewGuid();
        public bool Started; public Guid? SessionId; public long Sequence; public long? SourceMs; public DateTime? ObservedAt;
        public string? AnnotationContext; public string? AppliedFingerprint; public DateTime RetryAt; public int Failures;
        public (string,string,string,string?,string?)? AppliedSource;
        public readonly Dictionary<Guid,RuleEvaluationState> RuleStates=[];
        public readonly Dictionary<Guid,long> Samples=[];
        public readonly Dictionary<Guid,AiZoneMeasurement> Measurements=[];
        public readonly Dictionary<Guid,MonitoringRuleRuntimeView> RuleViews=[];
    }
}
