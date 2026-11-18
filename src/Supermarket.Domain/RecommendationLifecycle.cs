namespace Supermarket.Domain;

public static class RecommendationStatus
{
    public const string Available = "AVAILABLE";
    public const string Deferred = "DEFERRED";
    public const string Accepted = "ACCEPTED";
    public const string Dismissed = "DISMISSED";
    public const string Expired = "EXPIRED";
}

public static class RecommendationLifecycle
{
    private const int MaxReasonLength = 2000;

    public static string EffectiveStatus(string status, DateTime? expiresAtUtc, DateTime nowUtc)
    {
        Rules.Status(status, RecommendationStatus.Available, RecommendationStatus.Deferred,
            RecommendationStatus.Accepted, RecommendationStatus.Dismissed, RecommendationStatus.Expired);

        return status is RecommendationStatus.Available or RecommendationStatus.Deferred
            && expiresAtUtc is { } deadline && nowUtc >= deadline
            ? RecommendationStatus.Expired
            : status;
    }

    /// <summary>Validates a Manager decision and returns the normalized audit reason. The caller revalidates live evidence before accepting.</summary>
    public static string? ValidateDecision(string status, string decision, string? reason, DateTime? expiresAtUtc, DateTime nowUtc)
    {
        Rules.Status(decision, RecommendationStatus.Accepted, RecommendationStatus.Deferred, RecommendationStatus.Dismissed);
        var current = EffectiveStatus(status, expiresAtUtc, nowUtc);
        Rules.Require(current != RecommendationStatus.Expired, "RECOMMENDATION_EXPIRED", "The recommendation has expired.");
        Rules.Require(current == RecommendationStatus.Available
            || (current == RecommendationStatus.Deferred && decision != RecommendationStatus.Deferred),
            "INVALID_TRANSITION", "The recommendation cannot move to the requested status.");

        if (decision is RecommendationStatus.Deferred or RecommendationStatus.Dismissed)
            return Rules.Text(reason, MaxReasonLength, "Decision reason");

        var normalized = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Rules.Optional(normalized, MaxReasonLength, "Decision reason");
        return normalized;
    }
}
