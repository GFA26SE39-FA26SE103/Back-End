using System.Text.Json;
using Supermarket.Domain;

namespace Supermarket.Application;

public sealed record GenerateRecommendationRequest(Guid EventId);
public sealed record RecommendationGenerationResult(RecommendationView? Recommendation, string? Reason);
public sealed record RecommendationSignal(string EventType, string AlertLevel, decimal Metric, string Unit,
    decimal WarningThreshold, decimal CriticalThreshold, int SustainSeconds, string? SlaPriority);
public sealed record RecommendationChoice(string Action, string Confidence, string? Model = null);
public interface IRecommendationModel
{
    Task<RecommendationChoice> Choose(RecommendationSignal signal, IReadOnlyList<string> actions, CancellationToken ct);
}

public sealed record RecommendationRuleEvidence(Guid RuleId, DateTime RuleVersion, Guid ConfigId, DateTime ConfigVersion,
    string AlertLevel, Guid? PolicyId, int? PolicyVersion);

public sealed class RecommendationEvidenceReader(ISetupStore store, IClock clock)
{
    // A recent persisted observation is required both before model invocation and before acceptance.
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

    public async Task<(RecommendationSignal Signal, RecommendationRuleEvidence Basis)?> Read(OperationalEvent ev, CancellationToken ct)
    {
        if (ev.Status != "ACTIVE" || ev.ClearedAt is not null || ev.Priority is not ("WARNING" or "CRITICAL")
            || ev.RuleId is not { } ruleId || (await store.List<MonitoringRule>(r => r.RuleId == ruleId, ct)).SingleOrDefault() is not { Enabled: true } rule
            || rule.EventTypeId != ev.EventTypeId || rule.OutcomeType != "OPERATIONAL_EVENT"
            || (await store.List<MonitoringConfiguration>(c => c.ConfigId == rule.ConfigId, ct)).SingleOrDefault() is not { Status: "ACTIVE" } config || config.ZoneId != ev.ZoneId
            || (await store.List<OperationalEventType>(t => t.EventTypeId == ev.EventTypeId, ct)).SingleOrDefault() is not { Status: "ACTIVE" } type
            || !MonitoringPolicy.RuntimeDefinition(rule, type).Supported
            || rule.WarningThreshold < 0 || rule.CriticalThreshold <= rule.WarningThreshold
            || ev.MetricValue < (ev.Priority == "CRITICAL" ? rule.CriticalThreshold : rule.WarningThreshold)
            || !MatchesObservation(ev, rule, config)) return null;

        var policies = await store.List<SlaPolicy>(p => p.IsEnabled && p.ProblemDomain == "OPERATIONAL_EVENT" && p.EventTypeId == ev.EventTypeId, ct);
        if (policies.Count > 1) return null;
        var policy = policies.SingleOrDefault();
        var now = clock.UtcNow;
        if (ev.LastObservedAt > now || now - ev.LastObservedAt >= MaxAge) return null;
        return (new(type.Code, ev.Priority, ev.MetricValue, rule.ThresholdUnit, rule.WarningThreshold, rule.CriticalThreshold,
            rule.SustainSec, policy?.PriorityCode), new(rule.RuleId, rule.UpdatedAt, config.ConfigId, config.UpdatedAt, ev.Priority,
            policy?.PolicyId, policy?.Version));
    }

    private static bool MatchesObservation(OperationalEvent ev, MonitoringRule rule, MonitoringConfiguration config)
    {
        if (ev.MetadataJson is null) return false;
        try
        {
            using var data = JsonDocument.Parse(ev.MetadataJson);
            var root = data.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("configId", out var id) && id.ValueKind == JsonValueKind.String && id.TryGetGuid(out var configId) && configId == config.ConfigId
                && root.TryGetProperty("configurationVersion", out var version) && version.ValueKind == JsonValueKind.String
                && version.TryGetDateTime(out var versionAt) && versionAt == config.UpdatedAt
                && root.TryGetProperty("metric", out var metric) && metric.ValueKind == JsonValueKind.Number && metric.TryGetDecimal(out var value) && value == ev.MetricValue
                && root.TryGetProperty("thresholdUnit", out var unit) && unit.ValueKind == JsonValueKind.String && unit.GetString() == rule.ThresholdUnit;
        }
        catch (JsonException) { return false; }
    }
}
