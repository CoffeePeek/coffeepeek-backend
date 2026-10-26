using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Moderation.Domain.Tests;

public sealed class LeaderboardDecisionTests
{
    [Fact]
    public void ManualDecisionRequiresReasonAndIsSequencedWithoutChangingPublication()
    {
        var review = ModerationReview.CreateCheckInSnapshot(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            "user", "", null, null, null, null, DateTime.UtcNow, [], leaderboardGeoStatus: "OutsideRadius", shopDistanceMeters: 60);
        var admin = Guid.NewGuid(); var status = review.ModerationStatus; var sequence = review.DecisionSequence;
        Assert.Throws<DomainException>(() => review.SetLeaderboardDecision(true, " ", admin));
        Assert.Throws<DomainException>(() => review.SetLeaderboardDecision(true, "GPS issue", Guid.Empty));
        Assert.Throws<DomainException>(() => review.SetLeaderboardDecision(true, new string('x', 1001), admin));
        Assert.True(review.SetLeaderboardDecision(true, " GPS issue ", admin));
        Assert.Equal("GPS issue", review.LeaderboardReason);
        Assert.Equal(admin, review.ModeratedBy);
        Assert.Equal(sequence + 1, review.DecisionSequence);
        Assert.False(review.SetLeaderboardDecision(true, "GPS issue", admin));
        Assert.True(review.SetLeaderboardDecision(false, "Duplicate visit", admin));
        Assert.Equal(status, review.ModerationStatus);
        Assert.Equal(sequence + 2, review.DecisionSequence);
    }
}
