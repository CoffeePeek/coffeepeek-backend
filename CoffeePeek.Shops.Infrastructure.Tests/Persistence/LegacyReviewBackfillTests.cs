using System.Text.Json.Nodes;
using CoffeePeek.LegacyCheckInsMigration;
namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;
public class LegacyReviewBackfillTests
{
    public static JsonObject Fixture()
    {
        var user = "11111111-1111-1111-1111-111111111111";
        var shop = "22222222-2222-2222-2222-222222222222";
        var inventory = new JsonObject { ["Users"] = new JsonArray(new JsonObject { ["Id"] = user, ["Username"] = "Author", ["Statistics_CheckInCount"] = 9 }),
            ["Shops"] = new JsonArray(new JsonObject { ["Id"] = shop }), ["Drinks"] = new JsonArray(),
            ["Reviews"] = new JsonArray(), ["CheckIns"] = new JsonArray(), ["ModerationReviews"] = new JsonArray(),
            ["ReviewHelpfulVotes"] = new JsonArray(), ["ReviewReports"] = new JsonArray(),
            ["CanonicalCheckIns"] = new JsonArray(), ["ModerationCheckIns"] = new JsonArray() };
        for (var i = 1; i <= 5; i++)
        {
            var id = $"33333333-3333-3333-3333-{i:000000000000}";
            ((JsonArray)inventory["Reviews"]!).Add(new JsonObject {
                ["Id"] = id, ["UserId"] = user, ["CoffeeShopId"] = shop, ["UserName"] = "Author",
                ["Header"] = "retained header", ["Comment"] = "Legacy "+i, ["Rating_Place"] = 3, ["Rating_Service"] = 4,
                ["Rating_Coffee"] = 5, ["CreatedAtUtc"] = "2025-01-01T00:00:00Z", ["IsSoftDelete"] = i == 4,
                ["IsRemovedByAdmin"] = i == 5, ["HelpfulCount"] = 12 });
        }
        var reviews = (JsonArray)inventory["Reviews"]!;
        foreach (var i in new[]{2,3})
        {
            var mid = $"44444444-4444-4444-4444-{i:000000000000}";
            reviews[i-1]!["ModerationReviewId"] = mid;
            var m = (JsonObject)reviews[i-1]!.DeepClone(); m["Id"] = mid; m["ShopId"] = shop;
            m["ModerationStatus"] = i == 2 ? 0 : 2; m["IsSoftDelete"] = false;
            ((JsonArray)inventory["ModerationReviews"]!).Add(m);
        }
        return inventory;
    }
    [Fact]
    public void ApprovedUnlinkedReviewsRetainIdsAndEligibilityWhileLinkedPendingRejectedAndTombstonesStayHidden()
    {
        var inv = Fixture(); var original = inv.ToJsonString(); var plan = LegacyReviewBackfill.Map(inv);
        Assert.Equal(5, plan.Count(x => x!["Target"] is not null));
        var rows = plan.Where(x => x!["Target"] is not null).Select(x => x!["Target"]!).ToArray();
        Assert.Equal(inv["Reviews"]![0]!["Id"]!.ToString(), rows[0]["Id"]!.ToString());
        Assert.Equal(2, rows[0]["ModerationState"]!.GetValue<int>());
        Assert.Equal(1, rows[0]["Visibility"]!.GetValue<int>());
        Assert.Equal(4m, rows[0]["Rating_AverageRating"]!.GetValue<decimal>());
        Assert.Equal(1, rows[1]["ModerationState"]!.GetValue<int>());
        Assert.Equal(3, rows[2]["ModerationState"]!.GetValue<int>());
        Assert.NotNull(rows[3]["DeletedAtUtc"]); Assert.NotNull(rows[4]["DeletedAtUtc"]);
        Assert.Equal(original, inv.ToJsonString());
    }
}