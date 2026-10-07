using System.Text.Json.Nodes;
using CoffeePeek.LegacyCheckInMigration;

namespace CoffeePeek.LegacyCheckInMigration.Tests;

public class LegacyMigrationPlannerTests
{
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly Guid Shop = Guid.NewGuid();
    private static readonly Dictionary<Guid, string> Users = new() { [Author] = "tester" };
    private static readonly HashSet<Guid> Shops = [Shop];

    [Fact]
    public void CompleteVisitIsPrivateAndKeepsFrozenDrinkAndVisitTime()
    {
        var visit = Visit();
        var plan = Assert.Single(LegacyMigrationPlanner.Plan([visit], [], [], Users, Shops));
        Assert.Equal(visit.Id(), plan.CheckIn.Id());
        Assert.Equal(visit.Text("VisitedAt"), plan.CheckIn.Text("VisitedAt"));
        Assert.Equal("Эспрессо", plan.CheckIn.Text("DrinkNameRu"));
        Assert.Equal("tester", plan.CheckIn.Text("Username"));
        Assert.Equal(0, plan.CheckIn.Number("Visibility"));
        Assert.Equal(0, plan.CheckIn.Number("ModerationState"));
        Assert.Equal(4m, plan.CheckIn["Rating_AverageRating"]!.GetValue<decimal>());
    }

    [Theory]
    [InlineData(null, 3)]
    [InlineData("  ", 3)]
    [InlineData("test", null)]
    [InlineData("test", 0)]
    [InlineData("test", 6)]
    public void MissingTextOrScoreIsDiscardedWithoutInventingContent(string? text, int? rating)
    {
        var visit = Visit();
        visit["Note"] = text;
        visit["Rating_Coffee"] = rating;
        Assert.Empty(LegacyMigrationPlanner.Plan([visit], [], [], Users, Shops));
    }

    [Fact]
    public void MatchingReviewCompletesOldVisitAndPreservesApprovedRevision()
    {
        var visit = Visit();
        var review = Review();
        var snapshot = Snapshot(review);
        visit["ReviewId"] = review.Id().ToString();
        visit["Note"] = null;
        visit["Rating_Coffee"] = null;
        var plan = Assert.Single(LegacyMigrationPlanner.Plan([visit], [review], [snapshot], Users, Shops));
        Assert.Equal(visit.Id(), plan.CheckIn.Id());
        Assert.Equal("review text", plan.CheckIn.Text("Note"));
        Assert.Equal(1, plan.CheckIn.Number("Visibility"));
        Assert.Equal(2, plan.CheckIn.Number("ModerationState"));
        Assert.Equal(1, plan.CheckIn.Number("ApprovedRevision"));
        Assert.Equal(snapshot.Id(), plan.CheckIn.Id("ModerationSubmissionId"));
        Assert.Equal(visit.Id(), plan.Snapshot!.Id("CheckInId"));
    }

    [Fact]
    public void WrongAuthorLinkDoesNotMergeVisitsAndMissingApprovalStaysPrivate()
    {
        var visit = Visit();
        var review = Review();
        visit["ReviewId"] = review.Id().ToString();
        visit["UserId"] = Guid.NewGuid().ToString();
        var plan = Assert.Single(LegacyMigrationPlanner.Plan([visit], [review], [], Users, Shops));
        Assert.Equal(review.Id(), plan.CheckIn.Id());
        Assert.Null(plan.VisitId);
        Assert.Equal(0, plan.CheckIn.Number("Visibility"));
    }

    [Fact]
    public void ChangedDrinkSnapshotCannotApproveReviewAndDeletedRecordsAreDiscarded()
    {
        var review = Review();
        var snapshot = Snapshot(review);
        snapshot["DrinkNameRu"] = "Другой напиток";
        var plan = Assert.Single(LegacyMigrationPlanner.Plan([], [review], [snapshot], Users, Shops));
        Assert.Null(plan.Snapshot);
        Assert.Equal(0, plan.CheckIn.Number("Visibility"));
        review["IsRemovedByAdmin"] = true;
        Assert.Empty(LegacyMigrationPlanner.Plan([], [review], [snapshot], Users, Shops));
    }

    [Fact]
    public void DuplicateReviewLinksProduceOneMergedVisitAndKeepAnotherCompleteVisit()
    {
        var review = Review();
        var first = Visit();
        var second = Visit();
        first["ReviewId"] = review.Id().ToString();
        second["ReviewId"] = review.Id().ToString();
        first["Note"] = "review text";
        var plans = LegacyMigrationPlanner.Plan([first, second], [review], [], Users, Shops);
        Assert.Equal(2, plans.Count);
        Assert.Single(plans, x => x.ReviewId == review.Id());
        Assert.Equal(2, plans.Select(x => x.CheckIn.Id()).Distinct().Count());
    }

    [Fact]
    public void DifferentCompletePrivateNoteIsPreservedSeparatelyFromLinkedReview()
    {
        var review = Review();
        var visit = Visit();
        visit["ReviewId"] = review.Id().ToString();
        var plans = LegacyMigrationPlanner.Plan([visit], [review], [], Users, Shops);
        Assert.Equal(2, plans.Count);
        Assert.Contains(plans, x => x.CheckIn.Id() == visit.Id() && x.CheckIn.Text("Note") == "visit text");
        Assert.Contains(plans, x => x.CheckIn.Id() == review.Id() && x.CheckIn.Text("Note") == "review text");
    }

    [Fact]
    public void MissingShopAndAuthorAreDiscardedAndPlanningIsDeterministic()
    {
        var visit = Visit();
        Assert.Empty(LegacyMigrationPlanner.Plan([visit], [], [], Users, new HashSet<Guid>()));
        Assert.Empty(LegacyMigrationPlanner.Plan([visit], [], [], new Dictionary<Guid, string>(), Shops));
        var one = LegacyMigrationPlanner.Plan([visit], [], [], Users, Shops);
        var two = LegacyMigrationPlanner.Plan([visit], [], [], Users, Shops);
        Assert.True(JsonNode.DeepEquals(one[0].CheckIn, two[0].CheckIn));
    }

    public static JsonObject Visit() => new()
    {
        ["Id"] = Guid.NewGuid().ToString(), ["UserId"] = Author.ToString(), ["ShopId"] = Shop.ToString(),
        ["Note"] = "visit text", ["Rating_Place"] = 3, ["Rating_Service"] = 4, ["Rating_Coffee"] = 5,
        ["CreatedAtUtc"] = "2026-01-02T12:00:00Z", ["VisitedAt"] = "2026-01-01T12:00:00Z", ["DrinkNameRu"] = "Эспрессо"
    };

    public static JsonObject Review()
    {
        var review = Visit();
        review["CoffeeShopId"] = review["ShopId"]!.DeepClone();
        review["Comment"] = "review text";
        review["ModerationReviewId"] = Guid.NewGuid().ToString();
        return review;
    }

    public static JsonObject Snapshot(JsonObject review)
    {
        var snapshot = review.DeepClone().AsObject();
        snapshot["Id"] = review.Id("ModerationReviewId").ToString();
        snapshot["UserName"] = "tester";
        snapshot["ModerationStatus"] = 1;
        snapshot["ModeratedAt"] = "2026-01-03T12:00:00Z";
        return snapshot;
    }
}
