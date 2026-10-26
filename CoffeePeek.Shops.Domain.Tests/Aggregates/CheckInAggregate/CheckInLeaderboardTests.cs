using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.CheckInAggregate;

public sealed class CheckInLeaderboardTests
{
    private static readonly DateTime Now = DateTime.UtcNow;
    private static CheckIn Create() => CheckIn.CreateUnified(Guid.NewGuid(), Guid.NewGuid(), "author", "visit", null,
        Now, CheckInVisibility.Public);

    [Theory]
    [InlineData(0, true)]
    [InlineData(49.99, true)]
    [InlineData(50, true)]
    [InlineData(50.01, false)]
    [InlineData(1000, false)]
    public void RadiusIncludesFiftyMetersButApprovalIsStillRequired(double distance, bool expected)
    {
        var row = Create();
        row.VerifyLocation(distance / 6371008.8 * 180 / Math.PI, 0, 5, Now, 0, 0, Now);
        Assert.False(row.CountsForLeaderboard);
        row.ApplyModerationDecision(1, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, Now);
        Assert.True(row.IsPubliclyVisible);
        Assert.Equal(expected, row.CountsForLeaderboard);
        Assert.Equal(expected ? Now : (DateTime?)null, row.LeaderboardCreditedAtUtc);
    }

    [Theory]
    [InlineData(51, 0, "InaccurateLocation")]
    [InlineData(5, -301, "StaleLocation")]
    [InlineData(5, 61, "StaleLocation")]
    [InlineData(-1, 0, "InvalidLocation")]
    public void UnreliableLocationDoesNotPreventPublication(double accuracy, int ageSeconds, string reason)
    {
        var row = Create();
        row.VerifyLocation(0, 0, accuracy, Now.AddSeconds(ageSeconds), 0, 0, Now);
        row.ApplyModerationDecision(1, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, Now);
        Assert.True(row.IsPubliclyVisible);
        Assert.False(row.CountsForLeaderboard);
        Assert.Equal(reason, row.LeaderboardStatus);
    }

    [Fact]
    public void OverrideCanCreditMissingLocationAndRevokeWithoutChangingPublicationOrOriginalPeriod()
    {
        var row = Create(); var submission = Guid.NewGuid();
        row.ApplyModerationDecision(1, submission, 1, CheckInModerationState.Approved, null, Now);
        Assert.False(row.CountsForLeaderboard);
        row.ApplyModerationDecision(1, submission, 2, CheckInModerationState.Approved, null, Now, true);
        var version = row.LeaderboardVersion;
        Assert.True(row.CountsForLeaderboard);
        row.ApplyModerationDecision(1, submission, 3, CheckInModerationState.Approved, null, Now.AddDays(8), false);
        Assert.True(row.IsPubliclyVisible);
        Assert.False(row.CountsForLeaderboard);
        Assert.True(row.LeaderboardVersion > version);
        Assert.False(row.ApplyModerationDecision(1, submission, 2, CheckInModerationState.Approved, null, Now, true));
        row.ApplyModerationDecision(1, submission, 4, CheckInModerationState.Approved, null, Now.AddDays(9), true);
        Assert.Equal(Now, row.LeaderboardCreditedAtUtc);
        row.UpdateContent("changed", null);
        Assert.False(row.CountsForLeaderboard);
        Assert.Null(row.LeaderboardOverride);
        Assert.False(row.ApplyModerationDecision(1, submission, 5, CheckInModerationState.Approved, null, Now, true));
        row.ApplyModerationDecision(row.ContentRevision, Guid.NewGuid(), 1, CheckInModerationState.Approved, null, Now, true);
        Assert.True(row.CountsForLeaderboard);
        row.ChangeVisibility(CheckInVisibility.Private);
        Assert.False(row.CountsForLeaderboard);
        row.Delete();
        Assert.False(row.CountsForLeaderboard);
    }
}
