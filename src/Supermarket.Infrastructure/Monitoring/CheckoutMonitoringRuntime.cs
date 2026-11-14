using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Supermarket.Application;
using Supermarket.Domain;
using Supermarket.Domain.CheckoutCounters;
using AppError = Supermarket.Application.ApplicationException;

namespace Supermarket.Infrastructure.Monitoring;

/// <summary>Called only under the camera coordinator's gate. HTTP reads immutable published views.</summary>
public sealed class CheckoutMonitoringRuntime(IOptions<CheckoutMonitoringOptions> configured, IClock clock) : ICheckoutRuntimeState
{
    private readonly CheckoutMonitoringOptions options = configured.Value;
    private readonly Dictionary<Guid, ZoneJob> jobs = [];
    private readonly ConcurrentDictionary<Guid, CheckoutZoneRuntimeView> views = new();

    public CheckoutZoneRuntimeView? Get(Guid zoneId)
    {
        var view = views.GetValueOrDefault(zoneId);
        if (view is null || clock.UtcNow - view.GeneratedAt <= TimeSpan.FromSeconds(options.MaxObservationAgeSeconds)) return view;
        return view with { State = "UNAVAILABLE", IssueCode = "CHECKOUT_OBSERVATION_STALE", QueueCount = null,
            StaffedCounterCount = null, QueuePerStaffedCounter = null, NoStaffedCapacity = false,
            Counters = view.Counters.Select(c => c with { Presence = "UNKNOWN", QueueCount = null }).ToArray(),
            Rules = view.Rules.Select(r => r with { MetricValue = null, Reason = "CHECKOUT_OBSERVATION_STALE", CurrentLevel = null }).ToArray() };
    }

    public void Reconcile(MonitoringCameraSnapshot[] snapshots)
    {
        var groups = snapshots.SelectMany(s => (s.CheckoutMappings ?? []).Select(m => new Source(s, m)))
            .GroupBy(s => s.Mapping.ZoneId).ToArray();
        if (groups.Length > options.MaxZones) { Clear(); throw new AppError("CHECKOUT_ZONE_LIMIT", "Configured checkout-zone capacity exceeded.", 503); }
        var retained = groups.Select(g => g.Key).ToHashSet();
        foreach (var zone in jobs.Keys.Where(z => !retained.Contains(z)).ToArray()) { jobs.Remove(zone); views.TryRemove(zone, out _); }
        foreach (var group in groups)
        {
            var sources = group.OrderBy(s => s.Mapping.MappingId).ToArray();
            if (sources.Length > (long)options.MaxCountersPerZone * options.MaxSourcesPerCounter)
                throw new AppError("CHECKOUT_SOURCE_LIMIT", "Configured checkout-source capacity exceeded.", 503);
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                sources.Select(s => new { s.Snapshot.Fingerprint, s.Mapping })))));
            if (jobs.TryGetValue(group.Key, out var prior) && prior.Fingerprint == fingerprint) continue;
            var job = new ZoneJob(sources, fingerprint, clock.UtcNow);
            try
            {
                if (!options.AiContractEnabled) throw new AppError("CHECKOUT_AI_CONTRACT_DISABLED", "Checkout AI protocol v2 is not enabled.", 503);
                job.Issue = sources.Select(s => s.Mapping.IssueCode ?? s.Snapshot.IssueCode).FirstOrDefault(i => i is not null);
                if (sources.Select(s => s.Mapping.ConfigId).Distinct().Count() != 1
                    || sources.Select(s => s.Mapping.ConfigVersion).Distinct().Count() != 1)
                    throw new AppError("CHECKOUT_CONFIGURATION_CHANGED", "Checkout sources refer to different configuration versions.", 503);
                var counters = sources.GroupBy(s => s.Mapping.CounterId).ToArray();
                if (counters.Length > options.MaxCountersPerZone) throw new AppError("CHECKOUT_COUNTER_LIMIT", "Configured checkout-counter capacity exceeded.", 503);
                foreach (var counter in counters)
                    job.Counters.Add(counter.Key, new CheckoutCounterProjection(counter.Key,
                        counter.Select(s => new CheckoutObservationSource(s.Mapping.MappingId, s.Snapshot.CameraId, true, s.Mapping.IsQueueSource)).ToArray(),
                        new(TimeSpan.FromSeconds(options.PresentSeconds), TimeSpan.FromSeconds(options.AbsentSeconds),
                            TimeSpan.FromMilliseconds(options.MaxObservationGapMs), TimeSpan.FromSeconds(options.MaxObservationAgeSeconds),
                            TimeSpan.FromSeconds(options.MaxClockSkewSeconds), options.MaxSourcesPerCounter)));
            }
            catch (AppError e) { job.Issue = e.Code; }
            catch (DomainException e) { job.Issue = e.Code; }
            jobs[group.Key] = job;
            Publish(job, [], new(false, null, null, null, false, job.Issue ?? "CHECKOUT_OBSERVATIONS_PENDING"));
        }
    }

    public void Observe(MonitoringCameraSnapshot snapshot, AiMeasurementBatch batch, bool cameraUsable)
    {
        var expected = snapshot.CheckoutMappings ?? [];
        if (expected.Length == 0) return;
        if (batch.ProtocolVersion != 2 || batch.Checkouts is null || batch.Checkouts.Length != expected.Length
            || batch.Checkouts.Select(o => o.MappingId).Distinct().Count() != batch.Checkouts.Length
            || batch.ConfigurationFingerprint != snapshot.Fingerprint)
            throw Invalid();
        // Validate the complete batch before changing any source state.
        foreach (var observed in batch.Checkouts)
        {
            var mapping = expected.SingleOrDefault(m => m.MappingId == observed.MappingId);
            if (mapping is null || mapping.CounterId != observed.CounterId || mapping.ConfigId != observed.ConfigId
                || mapping.ConfigVersion != observed.ConfigVersion || mapping.MappingVersion != observed.MappingVersion
                || observed.QueueCount is < 0) throw Invalid();
        }
        foreach (var observed in batch.Checkouts)
        {
            var m = expected.Single(s => s.MappingId == observed.MappingId);
            if (!jobs.TryGetValue(m.ZoneId, out var job) || job.Issue is not null || !job.Counters.TryGetValue(m.CounterId, out var counter)) continue;
            var source = job.Sources.SingleOrDefault(s => s.Mapping.MappingId == m.MappingId && s.Snapshot.Fingerprint == snapshot.Fingerprint);
            if (source is null) throw Invalid();
            var accepted = counter.Observe(new(m.MappingId, batch.SessionId, batch.ContinuityId, batch.Sequence,
                batch.SourceElapsedMs, batch.CapturedAt, observed.PersonPresent, m.IsQueueSource ? observed.QueueCount : null,
                cameraUsable && observed.Usable, m.IsQueueSource ? observed.MaxWaitSeconds : null), clock.UtcNow);
            if (accepted && cameraUsable && observed.Usable)
            {
                job.Dirty = true;
                job.Origin = source;
            }
        }
    }

    public void Unavailable(Guid cameraId)
    {
        foreach (var job in jobs.Values)
            foreach (var source in job.Sources.Where(s => s.Snapshot.CameraId == cameraId))
                if (job.Counters.TryGetValue(source.Mapping.CounterId, out var counter))
                counter.Unavailable(source.Mapping.MappingId);
    }

    public async Task Evaluate(IServiceProvider services, CancellationToken ct)
    {
        foreach (var job in jobs.Values)
        {
            var counters = job.Counters.Values.Select(c => c.Read(clock.UtcNow)).ToArray();
            var metric = job.Issue is null ? CheckoutCapacity.Measure(counters, options.MaxCountersPerZone)
                : new CheckoutCapacityMeasurement(false, null, null, null, false, job.Issue);
            if (!metric.Available)
            {
                job.RuleStates.Clear(); job.Continuity = Guid.NewGuid(); job.Dirty = false;
                Publish(job, counters, metric); continue;
            }
            if (!job.Dirty || job.Origin is null)
            {
                Publish(job, counters, job.RuntimeIssue is null ? metric : new(false,null,null,null,false,job.RuntimeIssue));
                continue;
            }
            job.Sequence++;
            try
            {
                var origin = job.Origin;
                var writer = services.GetRequiredService<MonitoringIncidentWriter>();
                foreach (var rule in job.Rules)
                {
                    var validUntil = counters.Min(c => c.ValidUntil);
                    void EnsureFresh()
                    {
                        if (validUntil is null || clock.UtcNow > validUntil)
                            throw new AppError("CHECKOUT_OBSERVATION_STALE", "Checkout evidence expired during evaluation.", 503);
                    }
                    EnsureFresh();
                    decimal value;
                    if (rule.Mode == "WAITING_TIME")
                    {
                        if (counters.Any(c => c.MaxWaitSeconds is null))
                            throw new AppError("CHECKOUT_WAITING_SOURCE_INVALID", "Waiting-time source is not available.", 503);
                        value = decimal.Round((decimal)counters.Max(c => c.MaxWaitSeconds!.Value) / 60m, 4);
                    }
                    else
                    {
                        var parameters = CheckoutRuleParameters.Parse(rule.ParametersJson);
                        value = metric.QueueCount < parameters.MinQueue ? 0m
                            : metric.NoStaffedCapacity ? parameters.NoStaffedAlertLevel == "CRITICAL" ? rule.CriticalThreshold : rule.WarningThreshold
                            : decimal.Round(metric.QueuePerStaffedCounter!.Value, 4);
                    }
                    var elapsed = Math.Max(0, (long)(clock.UtcNow - job.StartedAt).TotalMilliseconds);
                    var before = job.RuleStates.GetValueOrDefault(rule.RuleId) ?? new RuleEvaluationState();
                    var evaluation = MonitoringRuleEvaluator.Evaluate(rule.Settings(), before,
                        new(job.Session, job.Continuity, job.Sequence, elapsed, value, job.Fingerprint, false));
                    var prior = job.RuleViews.GetValueOrDefault(rule.RuleId);
                    var shouldWrite = evaluation.ShouldClear || evaluation.EligibleSeverity != prior?.OperationalEventPriority
                        || !job.LastWrite.TryGetValue(rule.RuleId, out var last) || clock.UtcNow - last >= TimeSpan.FromSeconds(1);
                    var cooldown = 0m;
                    if (evaluation.EligibleSeverity is not null && prior?.OperationalEventId is null)
                    {
                        if(!job.History.TryGetValue(rule.RuleId,out var history) || clock.UtcNow-history.CheckedAt>=TimeSpan.FromSeconds(1))
                        {
                            var latest=await services.GetRequiredService<ICheckoutEventHistory>().LatestClearedAt(rule.RuleId,job.Zone.ZoneId,ct);
                            history=(clock.UtcNow,latest); job.History[rule.RuleId]=history;
                        }
                        var ended=history.ClearedAt;
                        cooldown = ended is null ? 0 : Math.Max(0, rule.CooldownSec - (decimal)(clock.UtcNow - ended.Value).TotalSeconds);
                    }
                    IncidentWriteResult? written = null;
                    EnsureFresh();
                    if (shouldWrite && cooldown == 0)
                    {
                        var capturedAt = validUntil!.Value.AddSeconds(-options.MaxObservationAgeSeconds);
                        var batch = new AiMeasurementBatch(2, job.Session, job.Continuity, job.Sequence, capturedAt, elapsed, job.Fingerprint, []);
                        var zone = new MonitoringZoneSnapshot(job.Zone.ZoneId, job.Zone.ZoneName, job.Zone.ConfigId, job.Zone.ConfigVersion,
                            [], job.Zone.Confidence, job.Rules);
                        var ruleOrigin = rule.Mode == "WAITING_TIME"
                            ? job.Sources.Single(s => s.Mapping.IsQueueSource) : origin;
                        written = await writer.Apply(new(ruleOrigin.Snapshot, zone, rule, batch,
                            new(zone.ZoneId, zone.ConfigId, zone.ConfigVersion, 0, 0), evaluation, true, value,
                            new { metric.QueueCount, metric.StaffedCounterCount, metric.QueuePerStaffedCounter, metric.NoStaffedCapacity,
                                maxWaitSeconds = counters.Max(c => c.MaxWaitSeconds),
                                sourceCameraIds = job.Sources.Select(s => s.Snapshot.CameraId).Distinct().Order().ToArray(),
                                sourceMappingIds = job.Sources.Select(s => s.Mapping.MappingId).Order().ToArray(), counters },
                            job.Sources.Select(s=>s.Snapshot).DistinctBy(s=>s.CameraId).ToArray(), validUntil), ct);
                        if (written.Stale) throw new AppError("CHECKOUT_CONFIGURATION_CHANGED", "Checkout configuration changed.", 409);
                        job.LastWrite[rule.RuleId] = clock.UtcNow;
                        if(evaluation.ShouldClear) job.History[rule.RuleId]=(clock.UtcNow,clock.UtcNow);
                    }
                    job.RuleStates[rule.RuleId] = evaluation.NextState;
                    var eventId = evaluation.ShouldClear && written is not null ? null : written?.OperationalEventId ?? prior?.OperationalEventId;
                    job.RuleViews[rule.RuleId] = new MonitoringRuleRuntimeView(rule.RuleId, rule.Code, rule.Name, rule.Mode, rule.Unit, rule.WarningThreshold, rule.CriticalThreshold,
                        rule.SustainSec, rule.CooldownSec, rule.ClearDurationSec ?? rule.SustainSec, value,
                        evaluation.WarningProgressMs, evaluation.CriticalProgressMs, cooldown,
                        cooldown > 0 ? "COOLDOWN" : written?.Reason ?? evaluation.Reason, OperationalEventPriority: evaluation.ShouldClear ? null : written?.OperationalEventPriority ?? prior?.OperationalEventPriority,
                        OperationalEventId: eventId).WithExits(rule) with { CurrentLevel = evaluation.ShouldClear ? null : evaluation.EligibleSeverity };
                }
                job.Dirty = false;
                job.RuntimeIssue = null;
                Publish(job, counters, metric);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                // Do not turn SQL/transport failures into recovery or replay stale observations.
                job.RuleStates.Clear(); job.Continuity = Guid.NewGuid(); job.Dirty = false;
                job.RuntimeIssue=error is AppError app ? app.Code : error is DomainException domain ? domain.Code : "CHECKOUT_MONITORING_UNAVAILABLE";
                Publish(job, counters, new(false, null, null, null, false,job.RuntimeIssue));
            }
        }
    }

    public void Clear() { jobs.Clear(); views.Clear(); }
    private void Publish(ZoneJob job, CheckoutCounterObservation[] counters, CheckoutCapacityMeasurement metric) =>
        views[job.Zone.ZoneId] = new(job.Zone.ZoneId, job.Zone.ConfigId, job.Zone.ConfigVersion, clock.UtcNow,
            metric.Available ? "READY" : "UNAVAILABLE", metric.IssueCode, metric.QueueCount, metric.StaffedCounterCount,
            metric.QueuePerStaffedCounter, metric.NoStaffedCapacity,
            counters.Select(c => new CheckoutCounterRuntimeView(c.CounterId, c.Presence.ToString().ToUpperInvariant(), c.QueueCount, c.Disagreement)).ToArray(),
            job.Rules.Select(r => job.RuleViews.GetValueOrDefault(r.RuleId) ?? new MonitoringRuleRuntimeView(r.RuleId, r.Code, r.Name, r.Mode, r.Unit,
                r.WarningThreshold, r.CriticalThreshold, r.SustainSec, r.CooldownSec, r.ClearDurationSec ?? r.SustainSec, null, 0, 0, 0, metric.IssueCode ?? "STARTING").WithExits(r))
                .Select(view=>metric.Available?view:view with {MetricValue=null,Reason=metric.IssueCode??"CHECKOUT_UNAVAILABLE",WarningProgressMs=0,CriticalProgressMs=0,CurrentLevel=null}).ToArray(),
            metric.Available && counters.All(c => c.MaxWaitSeconds is not null) ? counters.Max(c => c.MaxWaitSeconds) : null);

    private static AppError Invalid() => new("CHECKOUT_MEASUREMENT_INVALID", "Checkout observation does not match the active source configuration.", 503);
    private sealed record Source(MonitoringCameraSnapshot Snapshot, CheckoutMappingSnapshot Mapping);
    private sealed class ZoneJob(Source[] sources, string fingerprint, DateTime startedAt)
    {
        public Source[] Sources = sources;
        public CheckoutMappingSnapshot Zone = sources[0].Mapping;
        public MonitoringRuleSnapshot[] Rules = sources[0].Mapping.Rules;
        public string Fingerprint = fingerprint;
        public DateTime StartedAt = startedAt;
        public Guid Session = Guid.NewGuid(), Continuity = Guid.NewGuid();
        public long Sequence;
        public string? Issue;
        public string? RuntimeIssue;
        public bool Dirty;
        public Source? Origin;
        public Dictionary<Guid, CheckoutCounterProjection> Counters = [];
        public Dictionary<Guid, RuleEvaluationState> RuleStates = [];
        public Dictionary<Guid, MonitoringRuleRuntimeView> RuleViews = [];
        public Dictionary<Guid, DateTime> LastWrite = [];
        public Dictionary<Guid, (DateTime CheckedAt,DateTime? ClearedAt)> History = [];
    }
}
