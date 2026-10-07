using System.Text.Json.Nodes;

namespace CoffeePeek.LegacyCheckInMigration;

public static class LegacyMigrationPlanner
{
    public static List<LegacyCheckIn> Plan(IReadOnlyList<JsonObject> visits, IReadOnlyList<JsonObject> reviews,
        IReadOnlyList<JsonObject> moderation, IReadOnlyDictionary<Guid, string> users, ISet<Guid> shops)
    {
        var result = new List<LegacyCheckIn>();
        var usedVisits = new HashSet<Guid>();
        var usedSnapshots = new HashSet<Guid>();
        var snapshots = moderation.ToDictionary(x => x.Id());
        bool HasParents(JsonObject row, string shop) => users.ContainsKey(row.Id("UserId")) && shops.Contains(row.Id(shop));
        foreach (var review in reviews.OrderBy(x => x.Text("CreatedAtUtc")).ThenBy(x => x.Id()))
        {
            if (review.Flag("IsSoftDelete") || review.Flag("IsRemovedByAdmin") ||
                !LegacyRow.IsValidContent(review, "Comment") || !HasParents(review, "CoffeeShopId")) continue;
            var visit = visits.Where(x => !usedVisits.Contains(x.Id()) && x.Id("ReviewId") == review.Id() &&
                x.Id("UserId") == review.Id("UserId") && x.Id("ShopId") == review.Id("CoffeeShopId") &&
                (!LegacyRow.IsValidContent(x, "Note") || LegacyRow.SameContent(review, "Comment", x, "Note")))
                .OrderBy(x => x.Text("VisitedAt")).ThenBy(x => x.Id()).FirstOrDefault();
            if (visit != null) usedVisits.Add(visit.Id());
            snapshots.TryGetValue(review.Id("ModerationReviewId"), out var snapshot);
            if (snapshot != null && (snapshot.Flag("IsSoftDelete") || !LegacyRow.SameContent(review, "Comment", snapshot, "Comment") ||
                !LegacyRow.IsValidContent(snapshot, "Comment") || snapshot.Number("ModerationStatus") is not (0 or 1 or 2) ||
                !usedSnapshots.Add(snapshot.Id()))) snapshot = null;
            result.Add(Create(visit, review, snapshot, users));
        }
        foreach (var visit in visits.Where(x => !usedVisits.Contains(x.Id())))
        {
            if (!LegacyRow.IsValidContent(visit, "Note") || !HasParents(visit, "ShopId")) continue;
            result.Add(Create(visit, null, null, users));
        }
        return result;
    }

    private static LegacyCheckIn Create(JsonObject? visit, JsonObject? review, JsonObject? snapshot, IReadOnlyDictionary<Guid, string> users)
    {
        var content = review ?? visit!;
        var id = visit?.Id() ?? review!.Id();
        var row = content.Pick("DrinkSlug", "CustomDrinkName", "DrinkNameRu", "DrinkNameEn", "UserId",
            "Rating_Place", "Rating_Service", "Rating_Coffee", "CreatedAtUtc", "UpdatedAtUtc");
        row["Id"] = id.ToString();
        row["ShopId"] = (review?.Id("CoffeeShopId") ?? visit!.Id("ShopId")).ToString();
        row["VisitedAt"] = visit?["VisitedAt"]?.DeepClone() ?? content["CreatedAtUtc"]!.DeepClone();
        row["Note"] = content.Text(review != null ? "Comment" : "Note")!.Trim();
        row["Username"] = users[content.Id("UserId")];
        row["ContentRevision"] = 1;
        var status = snapshot?.Number("ModerationStatus");
        row["Visibility"] = status == 1 ? 1 : 0;
        row["ModerationState"] = status switch { 0 => 1, 1 => 2, 2 => 3, _ => 0 };
        row["ApprovedRevision"] = status == 1 ? 1 : null;
        row["ModerationSubmissionId"] = snapshot?.Id().ToString();
        row["ModerationDecisionSequence"] = status is 1 or 2 ? 1 : 0;
        row["RejectionReason"] = snapshot?["RejectedReason"]?.DeepClone();
        row["DeletedAtUtc"] = null;
        row["HelpfulCount"] = 0;
        row["FirstPublishedAtUtc"] = status == 1 ? (snapshot!["ModeratedAt"] ?? content["CreatedAtUtc"])!.DeepClone() : null;
        row["RowVersion"] = LegacyRow.StableId(id.ToString()).ToString();
        row["Rating_AverageRating"] = (content.Number("Rating_Place")!.Value + content.Number("Rating_Service")!.Value + content.Number("Rating_Coffee")!.Value) / 3m;
        JsonObject? newSnapshot = null;
        if (snapshot != null)
        {
            newSnapshot = snapshot.Pick("Id", "Comment", "UserId", "UserName", "ShopId", "DrinkSlug", "CustomDrinkName", "DrinkNameRu", "DrinkNameEn",
                "Rating_Place", "Rating_Service", "Rating_Coffee", "RejectedReason", "ModeratedBy", "ModeratedAt", "ModerationStatus", "IsSoftDelete", "CreatedAtUtc", "UpdatedAtUtc");
            newSnapshot["Comment"] = row["Note"]!.DeepClone();
            newSnapshot["CheckInId"] = id.ToString();
            newSnapshot["ContentRevision"] = 1;
            newSnapshot["VisitedAtUtc"] = row["VisitedAt"]!.DeepClone();
            newSnapshot["DecisionSequence"] = row["ModerationDecisionSequence"]!.DeepClone();
            newSnapshot["Rating_AverageRating"] = row["Rating_AverageRating"]!.DeepClone();
        }
        return new(visit?.Id(), review?.Id(), snapshot?.Id(), row, newSnapshot);
    }
}
