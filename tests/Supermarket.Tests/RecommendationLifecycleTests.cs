using Supermarket.Domain;
using Xunit;
using static Supermarket.Domain.RecommendationStatus;

namespace Supermarket.Tests;

public class RecommendationLifecycleTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(Available, Accepted)]
    [InlineData(Available, Deferred)]
    [InlineData(Available, Dismissed)]
    [InlineData(Deferred, Accepted)]
    [InlineData(Deferred, Dismissed)]
    public void ValidDecisionNormalizesAuditReason(string status, string decision)
    {
        Assert.Equal("Reviewed", RecommendationLifecycle.ValidateDecision(status, decision, " Reviewed ", Now.AddTicks(1), Now));
    }

    [Theory]
    [InlineData(Deferred, Deferred)]
    [InlineData(Accepted, Accepted)]
    [InlineData(Accepted, Deferred)]
    [InlineData(Accepted, Dismissed)]
    [InlineData(Dismissed, Accepted)]
    [InlineData(Dismissed, Deferred)]
    [InlineData(Dismissed, Dismissed)]
    [InlineData(Expired, Accepted)]
    [InlineData(Expired, Deferred)]
    [InlineData(Expired, Dismissed)]
    public void InvalidTransitionsAreRejected(string status, string decision)
    {
        var error = Assert.Throws<DomainException>(() => RecommendationLifecycle.ValidateDecision(status, decision, "Reviewed", null, Now));
        Assert.Equal(status == Expired ? "RECOMMENDATION_EXPIRED" : "INVALID_TRANSITION", error.Code);
    }

    [Theory]
    [InlineData(Available, Expired)]
    [InlineData(Deferred, Expired)]
    [InlineData(Accepted, Accepted)]
    [InlineData(Dismissed, Dismissed)]
    [InlineData(Expired, Expired)]
    public void DeadlineOnlyExpiresOpenRecommendations(string status, string expected)
    {
        Assert.Equal(status, RecommendationLifecycle.EffectiveStatus(status, null, Now));
        Assert.Equal(status, RecommendationLifecycle.EffectiveStatus(status, Now.AddTicks(1), Now));
        Assert.Equal(expected, RecommendationLifecycle.EffectiveStatus(status, Now, Now));
        Assert.Equal(expected, RecommendationLifecycle.EffectiveStatus(status, Now.AddTicks(-1), Now));
    }

    [Theory]
    [InlineData(Available)]
    [InlineData(Deferred)]
    public void OpenRecommendationCannotBeAcceptedAtItsDeadline(string status)
    {
        var error = Assert.Throws<DomainException>(() => RecommendationLifecycle.ValidateDecision(status, Accepted, null, Now, Now));
        Assert.Equal("RECOMMENDATION_EXPIRED", error.Code);
    }

    [Theory]
    [InlineData(Deferred, null)]
    [InlineData(Deferred, " \t ")]
    [InlineData(Dismissed, null)]
    [InlineData(Dismissed, " \t ")]
    public void DeferringAndDismissingRequireReason(string decision, string? reason)
    {
        var error = Assert.Throws<DomainException>(() => RecommendationLifecycle.ValidateDecision(Available, decision, reason, null, Now));
        Assert.Equal("INVALID_FIELD", error.Code);
    }

    [Theory]
    [InlineData(Accepted)]
    [InlineData(Deferred)]
    [InlineData(Dismissed)]
    public void ReasonFitsAuditLogLimit(string decision)
    {
        var reason = new string('x', 2000);
        Assert.Equal(reason, RecommendationLifecycle.ValidateDecision(Available, decision, $" {reason} ", null, Now));
        var error = Assert.Throws<DomainException>(() => RecommendationLifecycle.ValidateDecision(Available, decision, reason + "x", null, Now));
        Assert.Equal("INVALID_FIELD", error.Code);
    }

    [Fact]
    public void AcceptReasonIsOptional()
    {
        Assert.Null(RecommendationLifecycle.ValidateDecision(Available, Accepted, " ", null, Now));
    }

    [Theory]
    [InlineData(Available, Available)]
    [InlineData(Available, Expired)]
    [InlineData(Available, "UNKNOWN")]
    [InlineData("UNKNOWN", Accepted)]
    public void UnsupportedStatusOrDecisionIsRejected(string status, string decision)
    {
        var error = Assert.Throws<DomainException>(() => RecommendationLifecycle.ValidateDecision(status, decision, null, null, Now));
        Assert.Equal("INVALID_STATUS", error.Code);
    }
}
