using System.Text.Json;
using Supermarket.Domain;

namespace Supermarket.Application;

public sealed record RecommendationQuery(string? Status = null, Guid? ZoneId = null, int Page = 1, int PageSize = 50);
public sealed record OperatorRecommendationQuery(Guid? ZoneId = null, int Page = 1, int PageSize = 50);
public sealed record RecommendationDecision(string Decision, string? Reason = null);
public sealed record RecommendationView(Guid RecommendationId, Guid? ZoneId, Guid? EventId, string RecommendationType,
    string Title, string Description, string Status, DateTime GeneratedAt, DateTime? ExpiresAt,
    Guid? ReviewedByUserId, DateTime? ReviewedAt, RecommendationDisplayEvidence? Evidence = null);
public sealed record RecommendationPage(RecommendationView[] Items, int Page, int PageSize, bool HasMore);
public sealed record RecommendationEventEvidence(Guid EventId, DateTime ObservedAt, DateTime UpdatedAt, DateTime ValidUntil,
    RecommendationRuleEvidence? RuleBasis = null);
public sealed record RecommendationDisplayEvidence(RecommendationSignal Signal, DateTime ObservedAt, string Confidence, int? SlaPolicyVersion);

public interface IRecommendationQueries
{
    Task<RecommendationPage> List(Guid supermarketId, RecommendationQuery query, DateTime now, CancellationToken ct);
}

public sealed class RecommendationWork(ISetupStore store, IRecommendationQueries queries, ICurrentUser user, IClock clock, TaskNotifier notifier,
    RecommendationEvidenceReader evidenceReader, IRecommendationModel model)
{
    public const string AcceptedNotification = "RECOMMENDATION_ACCEPTED";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<RecommendationGenerationResult> Generate(GenerateRecommendationRequest request, CancellationToken ct)
    {
        UseCase.Manager(user);
        if (request.EventId == Guid.Empty)
            throw new ApplicationException("RECOMMENDATION_EVENT_REQUIRED", "Choose an Operational Event.", 400);
        var supermarketId = await StoreId(ct);
        var initial = UseCase.Found(await ReadScopedEvent(request.EventId, supermarketId, ct));
        if (await evidenceReader.Read(initial, ct) is not { } source)
            return new(null, "INSUFFICIENT_VALID_EVIDENCE");

        string[] actions = source.Signal.EventType switch
        {
            "LONG_QUEUE" or "EXCESSIVE_WAIT" or "CHECKOUT_CAPACITY" => ["REVIEW_CHECKOUT_CAPACITY", "MONITOR_CONDITION", "NONE"],
            "CONGESTION" => ["INSPECT_ZONE", "MONITOR_CONDITION", "NONE"],
            _ => ["MONITOR_CONDITION", "NONE"]
        };
        var choice = await model.Choose(source.Signal, actions, ct);
        if (!actions.Contains(choice.Action) || choice.Confidence is not ("LOW" or "MEDIUM" or "HIGH"))
            throw new ApplicationException("RECOMMENDATION_AI_INVALID_RESPONSE", "The recommendation model returned an invalid choice.", 503);
        if (choice.Action == "NONE" || choice.Confidence == "LOW")
            return new(null, "NO_SUPPORTED_RECOMMENDATION");

        return await store.Transaction<RecommendationGenerationResult>(async () =>
        {
            var latest = await ReadScopedEvent(request.EventId, supermarketId, ct);
            if (latest is null || latest.ZoneId != initial.ZoneId || latest.LastObservedAt < initial.LastObservedAt
                || await evidenceReader.Read(latest, ct) is not { } current || current.Basis != source.Basis)
                return new(null, "EVIDENCE_CHANGED");

            var (title, description) = choice.Action switch
            {
                "REVIEW_CHECKOUT_CAPACITY" => ("Review checkout capacity", "Ask the Operator to check whether checkout support or an additional staffed counter is appropriate."),
                "INSPECT_ZONE" => ("Inspect the congested zone", "Ask the Operator to inspect customer flow and decide whether to intervene."),
                _ => ("Monitor the condition", "Ask the Operator to review the current condition before deciding whether to intervene.")
            };
            var now = clock.UtcNow;
            var expires = now.AddMinutes(5);
            var row = new Recommendation
            {
                RecommendationId = Guid.NewGuid(), SupermarketId = supermarketId, ZoneId = latest.ZoneId, EventId = latest.EventId,
                RecommendationType = choice.Action, Title = title, Description = description, GeneratedAt = now, ExpiresAt = expires,
                SupportingDataJson = JsonSerializer.Serialize(new
                {
                    EventEvidence = new RecommendationEventEvidence(latest.EventId, latest.LastObservedAt, latest.UpdatedAt, expires, current.Basis),
                    CurrentSignal = current.Signal, ModelInputSignal = source.Signal, choice.Confidence, choice.Model
                }, Json)
            };
            await store.Add(row, ct);
            return new(View(row, now), null);
        }, ct);
    }

    public Task<RecommendationPage> List(RecommendationQuery query, CancellationToken ct)
    {
        UseCase.Manager(user);
        return ListScoped(query, ct);
    }

    public Task<RecommendationPage> ListAccepted(OperatorRecommendationQuery query, CancellationToken ct)
    {
        UseCase.Operator(user);
        return ListScoped(new(RecommendationStatus.Accepted, query.ZoneId, query.Page, query.PageSize), ct);
    }

    private async Task<RecommendationPage> ListScoped(RecommendationQuery query, CancellationToken ct)
    {
        var status = query.Status?.Trim().ToUpperInvariant();
        if (status is not (null or RecommendationStatus.Available or RecommendationStatus.Deferred or RecommendationStatus.Accepted
            or RecommendationStatus.Dismissed or RecommendationStatus.Expired)
            || query.ZoneId == Guid.Empty || query.Page is < 1 or > 10000 || query.PageSize is < 1 or > 100)
            throw new ApplicationException("INVALID_RECOMMENDATION_QUERY", "Use a valid status or zone ID, page 1–10000 and pageSize 1–100.", 400);

        var supermarketId = await StoreId(ct);
        return await queries.List(supermarketId, query with { Status = status }, clock.UtcNow, ct);
    }

    public async Task<RecommendationView> Get(Guid id, CancellationToken ct)
    {
        UseCase.Manager(user);
        var row = await Find(id, await StoreId(ct), ct);
        return View(row, clock.UtcNow);
    }

    public async Task<RecommendationView> GetAccepted(Guid id, CancellationToken ct)
    {
        UseCase.Operator(user);
        var row = await Find(id, await StoreId(ct), ct);
        return View(UseCase.Found(row.Status == RecommendationStatus.Accepted ? row : null), clock.UtcNow);
    }

    public Task<RecommendationView> Decide(Guid id, RecommendationDecision request, CancellationToken ct)
    {
        UseCase.Manager(user);
        return store.Transaction(async () =>
        {
            var supermarketId = await StoreId(ct);
            var row = await Find(id, supermarketId, ct);
            var now = clock.UtcNow;
            var reason = ValidateDecision(row, request, now);
            if (request.Decision == RecommendationStatus.Accepted)
            {
                var evidence = await RevalidateEvent(row, supermarketId, ct);
                now = clock.UtcNow;
                ValidateDecision(row, request, now);
                if (evidence.ObservedAt > row.GeneratedAt || row.GeneratedAt > now || now >= evidence.ValidUntil)
                    throw EvidenceUnavailable();
            }

            var before = JsonSerializer.Serialize(new { row.Status, row.ReviewedByUserId, row.ReviewedAt }, Json);
            row.Status = request.Decision;
            row.ReviewedByUserId = user.UserId;
            row.ReviewedAt = now;
            await store.Update(row, ct);
            await store.Add(new AuditLog
            {
                AuditLogId = Guid.NewGuid(), UserId = user.UserId, Action = "RECOMMENDATION_DECISION",
                EntityType = "Recommendation", EntityId = row.RecommendationId, Reason = reason,
                OldValuesJson = before, NewValuesJson = JsonSerializer.Serialize(new { row.Status, row.ReviewedByUserId, row.ReviewedAt }, Json),
                ResultStatus = "SUCCESS", CreatedAt = now
            }, ct);
            if (row.Status == RecommendationStatus.Accepted)
                await NotifyOperators(row, ct);
            return View(row, now);
        }, ct);
    }

    private async Task NotifyOperators(Recommendation row, CancellationToken ct)
    {
        var operators = await notifier.Operators(ct);
        if (operators.Count == 0)
            return;

        await notifier.Broadcast(AcceptedNotification, "Manager accepted an operational recommendation",
            $"Review recommendation {row.RecommendationId}: {row.Title}", operators, AcceptedNotification, ct, eventId: row.EventId);
    }

    private async Task<Guid> StoreId(CancellationToken ct)
    {
        var supermarkets = await store.List<Domain.Supermarket>(ct: ct);
        if (supermarkets.Count != 1)
            throw new ApplicationException("RECOMMENDATION_SCOPE_UNAVAILABLE", "A single configured supermarket is required.", 503);
        return supermarkets[0].SupermarketId;
    }

    private async Task<Recommendation> Find(Guid id, Guid supermarketId, CancellationToken ct)
    {
        var row = await store.Find<Recommendation>(id, ct);
        return UseCase.Found(row?.SupermarketId == supermarketId ? row : null);
    }

    private async Task<OperationalEvent?> ReadScopedEvent(Guid id, Guid supermarketId, CancellationToken ct)
    {
        // List uses a no-tracking SQL query so the second read observes changes made during the model call.
        var ev = (await store.List<OperationalEvent>(e => e.EventId == id, ct)).SingleOrDefault();
        if (ev is null || (await store.List<Zone>(z => z.ZoneId == ev.ZoneId, ct)).SingleOrDefault() is not { } zone
            || (await store.List<Floor>(f => f.FloorId == zone.FloorId, ct)).SingleOrDefault() is not { } floor
            || floor.SupermarketId != supermarketId) return null;
        return ev;
    }

    private static string? ValidateDecision(Recommendation row, RecommendationDecision request, DateTime now)
    {
        try { return RecommendationLifecycle.ValidateDecision(row.Status, request.Decision, request.Reason, row.ExpiresAt, now); }
        catch (DomainException error) when (error.Code is "INVALID_TRANSITION" or "RECOMMENDATION_EXPIRED")
        { throw new ApplicationException(error.Code, error.Message); }
    }

    private async Task<RecommendationEventEvidence> RevalidateEvent(Recommendation row, Guid supermarketId, CancellationToken ct)
    {
        var evidence = ReadEvidence(row.SupportingDataJson);
        if (evidence is null || row.EventId != evidence.EventId || row.ZoneId is not { } zoneId
            || await store.Find<OperationalEvent>(evidence.EventId, ct) is not { } ev
            || ev.Status != "ACTIVE" || ev.ClearedAt is not null || ev.ZoneId != zoneId
            || await store.Find<Zone>(zoneId, ct) is not { } zone
            || await store.Find<Floor>(zone.FloorId, ct) is not { } floor || floor.SupermarketId != supermarketId)
            throw EvidenceUnavailable();

        if (evidence.RuleBasis is { } basis)
        {
            if (ev.LastObservedAt < evidence.ObservedAt || await evidenceReader.Read(ev, ct) is not { } current || current.Basis != basis)
                throw EvidenceUnavailable();
        }
        else if (ev.LastObservedAt != evidence.ObservedAt || ev.UpdatedAt != evidence.UpdatedAt)
            throw EvidenceUnavailable();

        return evidence;
    }

    private static RecommendationEventEvidence? ReadEvidence(string? json)
    {
        if (json is null) return null;
        try
        {
            using var data = JsonDocument.Parse(json);
            return data.RootElement.ValueKind == JsonValueKind.Object && data.RootElement.TryGetProperty("eventEvidence", out var evidence)
                ? evidence.Deserialize<RecommendationEventEvidence>(Json) : null;
        }
        catch (JsonException) { return null; }
    }

    private static ApplicationException EvidenceUnavailable() => new("RECOMMENDATION_EVIDENCE_UNAVAILABLE",
        "The source evidence is unavailable or has changed. Request a new recommendation before accepting.");

    private static RecommendationDisplayEvidence? DisplayEvidence(string? json)
    {
        if (ReadEvidence(json) is not { RuleBasis: not null } evidence) return null;
        try
        {
            using var data = JsonDocument.Parse(json!);
            var root = data.RootElement;
            if (!root.TryGetProperty("currentSignal", out var signal) || signal.Deserialize<RecommendationSignal>(Json) is not { } value
                || !root.TryGetProperty("confidence", out var confidence) || confidence.ValueKind != JsonValueKind.String
                || confidence.GetString() is not ("MEDIUM" or "HIGH")) return null;
            return new(value, evidence.ObservedAt, confidence.GetString()!, evidence.RuleBasis.PolicyVersion);
        }
        catch (JsonException) { return null; }
    }

    public static RecommendationView View(Recommendation row, DateTime now) => new(row.RecommendationId, row.ZoneId,
        row.EventId, row.RecommendationType, row.Title, row.Description,
        RecommendationLifecycle.EffectiveStatus(row.Status, row.ExpiresAt, now), DateTime.SpecifyKind(row.GeneratedAt, DateTimeKind.Utc),
        Utc(row.ExpiresAt), row.ReviewedByUserId, Utc(row.ReviewedAt), DisplayEvidence(row.SupportingDataJson));

    private static DateTime? Utc(DateTime? value) => value is { } date ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : null;
}
