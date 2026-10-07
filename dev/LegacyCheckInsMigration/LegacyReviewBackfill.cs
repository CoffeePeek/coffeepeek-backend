using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoffeePeek.Shared.Kernel.Drinks;
using Npgsql;
using NpgsqlTypes;

namespace CoffeePeek.LegacyCheckInsMigration;

// Operator-only copy. No events are replayed and no archived row is updated.
public static partial class LegacyReviewBackfill
{
    public static IEnumerable<JsonObject> Rows(JsonObject inventory, string table) =>
        (inventory[table] as JsonArray ?? []).OfType<JsonObject>();
    public static string? Text(JsonObject row, string key) => row[key]?.ToString();
    public static int Int(JsonObject row, string key) => int.TryParse(Text(row, key), out var n) ? n : 0;
    public static bool Flag(JsonObject row, string key) => Text(row, key) == "true";
    public static Guid StableId(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes("legacy-check-ins/v1/" + value))[..16]);

    public static JsonArray Map(JsonObject inventory)
    {
        var result = new JsonArray();
        var reviews = Rows(inventory, "Reviews").ToArray();
        var visits = Rows(inventory, "CheckIns").ToArray();
        var moderation = Rows(inventory, "ModerationReviews").ToArray();
        var consumedVisits = new HashSet<string>();
        var consumedModeration = new HashSet<string>();
        foreach (var review in reviews)
        {
            var id = Text(review, "Id")!;
            var links = visits.Where(v => Text(v, "ReviewId") == id).ToArray();
            var mid = Text(review, "ModerationReviewId");
            var linked = moderation.Where(m => Text(m, "Id") == mid).ToArray();
            var collision = links.Length > 1 || links.Any(v => !Identity(review, v))
                || (mid != null && (linked.Length != 1 || !Identity(review, linked[0])
                    || reviews.Count(r => Text(r, "ModerationReviewId") == mid) != 1));
            // An explicit bad link must not be guessed away.
            var error = collision ? "conflicting/missing explicit source link" : Validate(review, inventory, "Comment");
            if (error == null && linked.Length == 1) error = Validate(linked[0], inventory, "Comment");
            if (error != null) { result.Add(Outcome("Reviews", id, "unresolved", error)); continue; }
            var m = linked.SingleOrDefault();
            if (m != null) consumedModeration.Add(mid!);
            if (links.Length == 1) consumedVisits.Add(Text(links[0], "Id")!);
            var content = m ?? review;
            var different = m != null && (ContentHash(review) != ContentHash(m)
                || PublishedPhotoKeys(inventory,id,links.SingleOrDefault())
                    != PhotoKeys(inventory, "ModerationPhotos", "ModerationReviewId", mid!));
            var revision = different ? 2 : 1;
            var deleted = Flag(review, "IsSoftDelete") || Flag(review, "IsRemovedByAdmin") || (m != null && Flag(m, "IsSoftDelete"));
            var legacyStatus = m == null ? 1 : Int(m, "ModerationStatus");
            var state = legacyStatus switch { 0 => 1, 1 => 2, 2 => 3, _ => -1 };
            if (state < 0) { result.Add(Outcome("Reviews", id, "unresolved", "unknown moderation status")); continue; }
            var visited = links.SingleOrDefault()?["VisitedAt"] ?? review["CreatedAtUtc"];
            var created = links.SingleOrDefault()?["CreatedAtUtc"] ?? review["CreatedAtUtc"];
            var snapshots = new JsonArray();
            if (different) snapshots.Add(Snapshot(review, id, StableId(id + "/published").ToString(), 1, 1, visited!, null));
            var submission = mid ?? StableId(id + "/published").ToString();
            snapshots.Add(Snapshot(content, id, submission, revision, legacyStatus, visited!, m));
            var target = Visit(content, id, review, visited!, created!, revision, state, deleted, submission, true);
            var entry = Outcome("Reviews", id, "mapped", null);
            entry["Target"] = target; entry["Snapshots"] = snapshots;
            entry["TimestampPolicy"] = links.Length == 1 ? "explicit CheckIns.ReviewId" : "Reviews.CreatedAtUtc fallback";
            if (Flag(review, "IsSoftDelete")) entry["Ambiguity"] = "legacy soft delete may be edit hiding or deletion; retained tombstone";
            if (m != null) entry["ModerationSourceId"] = mid;
            if (links.Length == 1) entry["VisitSourceId"] = Text(links[0], "Id");
            result.Add(entry);
        }
        foreach (var m in moderation)
        {
            var id = Text(m, "Id")!;
            if (consumedModeration.Contains(id)) { result.Add(Outcome("ModerationReviews", id, "merged", null)); continue; }
            if (reviews.Any(r => Text(r, "ModerationReviewId") == id))
            { result.Add(Outcome("ModerationReviews", id, "unresolved", "linked review did not map")); continue; }
            var error = Validate(m, inventory, "Comment");
            var state = Int(m, "ModerationStatus") switch { 0 => 1, 1 => 2, 2 => 3, _ => -1 };
            if (state < 0) error = "unknown moderation status";
            if (error != null) { result.Add(Outcome("ModerationReviews", id, "unresolved", error)); continue; }
            var entry = Outcome("ModerationReviews", id, "mapped", null);
            entry["Target"] = Visit(m, id, m, m["CreatedAtUtc"]!, m["CreatedAtUtc"]!, 1, state,
                Flag(m, "IsSoftDelete"), id, true);
            entry["Snapshots"] = new JsonArray(Snapshot(m, id, id, 1, Int(m, "ModerationStatus"), m["CreatedAtUtc"]!, m));
            entry["ModerationSourceId"] = id; entry["TimestampPolicy"] = "ModerationReviews.CreatedAtUtc fallback";
            result.Add(entry);
        }
        foreach (var v in visits)
        {
            var id = Text(v, "Id")!;
            if (consumedVisits.Contains(id)) { result.Add(Outcome("CheckIns", id, "merged", null)); continue; }
            var error = Text(v, "ReviewId") != null ? "explicit review link did not map" : Validate(v, inventory, "Note");
            if (error != null) { result.Add(Outcome("CheckIns", id, "invalid", error)); continue; }
            var entry = Outcome("CheckIns", id, "mapped", null);
            var visitContent = (JsonObject)v.DeepClone(); visitContent["Username"] = Text(Rows(inventory,"Users").Single(u => Text(u,"Id") == Text(v,"UserId")),"Username");
            entry["Target"] = Visit(visitContent, id, v, v["VisitedAt"]!, v["CreatedAtUtc"]!, 1, 0, false, null, false);
            entry["Snapshots"] = new JsonArray(); entry["VisitSourceId"] = id; entry["TimestampPolicy"] = "CheckIns.VisitedAt";
            result.Add(entry);
        }
        // UUID identity is global within canonical visits; duplicate sources never win by iteration order.
        foreach (var group in result.OfType<JsonObject>().Where(e => e["Target"] != null).GroupBy(e => Text(e["Target"]!.AsObject(), "Id")))
        {
            if (group.Count() < 2) continue;
            foreach (var e in group) { e["Outcome"] = "conflict"; e["Reason"] = "canonical UUID collides across legacy sources"; e.Remove("Target"); e.Remove("Snapshots"); }
        }
        foreach (var entry in result.OfType<JsonObject>())
        {
            var source = Rows(inventory,Text(entry,"SourceTable")!).Single(r => Text(r,"Id") == Text(entry,"SourceId"));
            entry["SourceHash"] = Hash(source);
        }

        return result;
    }

    private static bool Identity(JsonObject a, JsonObject b) =>
        Text(a, "UserId") == Text(b, "UserId") && (Text(a, "CoffeeShopId") ?? Text(a, "ShopId")) == Text(b, "ShopId");
    private static string? Validate(JsonObject r, JsonObject inventory, string note)
    {
        if (!Guid.TryParse(Text(r, "Id"), out var id) || id == Guid.Empty) return "invalid source UUID";
        if (!Rows(inventory, "Shops").Any(s => Text(s, "Id") == (Text(r, "CoffeeShopId") ?? Text(r, "ShopId")))) return "missing shop";
        if (!Rows(inventory, "Users").Any(s => Text(s, "Id") == Text(r, "UserId"))) return "missing account";
        var text = Text(r, note);
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1000) return "incomplete/oversized note";
        if (new[] { "Rating_Place", "Rating_Service", "Rating_Coffee" }.Any(k => Int(r, k) is < 1 or > 5)) return "incomplete ratings";
        if (!DateTimeOffset.TryParse(Text(r, "CreatedAtUtc"), out _) || (note == "Note" && !DateTimeOffset.TryParse(Text(r, "VisitedAt"), out _))) return "invalid visit timestamp";
        var username = Text(r, "UserName") ?? Text(Rows(inventory, "Users").Single(u => Text(u,"Id") == Text(r,"UserId")), "Username");
        if (string.IsNullOrWhiteSpace(username) || username.Length > 30) return "missing/oversized author name";
        var slug = Text(r, "DrinkSlug");
        var custom = Text(r, "CustomDrinkName");
        if (new[] { "CustomDrinkName", "DrinkNameRu", "DrinkNameEn" }.Any(k => Text(r,k)?.Length > DrinkSelection.MaxNameLength)) return "oversized drink metadata";
        if (slug == DrinkSelection.OtherSlug)
        {
            if (string.IsNullOrWhiteSpace(custom)) return "incomplete Other drink name";
        }
        else
        {
            if (custom != null) return "conflicting drink selections";
            if (slug != null && (slug.Length > DrinkSelection.MaxSlugLength || !Rows(inventory, "Drinks").Any(d => Text(d, "Slug") == slug))) return "unknown drink slug";
        }
        return null;
    }
    private static JsonObject Visit(JsonObject content, string id, JsonObject source, JsonNode visited, JsonNode created,
        int revision, int state, bool deleted, string? submission, bool isPublic)
    {
        var row = Content(content);
        row["Id"] = id; row["Note"] = Text(content, "Comment") ?? Text(content, "Note"); row["UserId"] = Text(content, "UserId");
        row["ShopId"] = Text(content, "CoffeeShopId") ?? Text(content, "ShopId"); row["VisitedAt"] = visited.DeepClone();
        row["CreatedAtUtc"] = created.DeepClone(); row["UpdatedAtUtc"] = content["UpdatedAtUtc"]?.DeepClone();
        row["Username"] = Text(content, "UserName") ?? Text(content, "Username") ?? "";
        row["Visibility"] = isPublic ? 1 : 0; row["ContentRevision"] = revision; row["ModerationState"] = state;
        row["ApprovedRevision"] = state == 2 ? revision : null; row["ModerationSubmissionId"] = submission;
        row["ModerationDecisionSequence"] = state is 2 or 3 ? 1L : 0L; row["RejectionReason"] = Text(content, "RejectedReason");
        row["DeletedAtUtc"] = deleted ? (source["UpdatedAtUtc"] ?? source["CreatedAtUtc"])!.DeepClone() : null;
        row["HelpfulCount"] = 0; row["FirstPublishedAtUtc"] = isPublic && !deleted && state == 2 ? source["CreatedAtUtc"]!.DeepClone() : null;
        row["RowVersion"] = StableId(id + "/visit").ToString();
        return row;
    }
    private static JsonObject Snapshot(JsonObject c, string visit, string id, int revision, int status, JsonNode visited, JsonObject? audit)
    {
        var row = Content(c);
        row["Id"] = id; row["Comment"] = Text(c, "Comment") ?? Text(c, "Note"); row["UserId"] = Text(c, "UserId");
        row["UserName"] = Text(c, "UserName") ?? Text(c, "Username") ?? ""; row["ShopId"] = Text(c, "CoffeeShopId") ?? Text(c,"ShopId");
        row["CheckInId"] = visit; row["ContentRevision"] = revision; row["VisitedAtUtc"] = visited.DeepClone();
        row["DecisionSequence"] = status is 1 or 2 ? 1L : 0L; row["ModerationStatus"] = status;
        row["RejectedReason"] = audit?["RejectedReason"]?.DeepClone(); row["ModeratedBy"] = audit?["ModeratedBy"]?.DeepClone();
        row["ModeratedAt"] = audit?["ModeratedAt"]?.DeepClone(); row["IsSoftDelete"] = Flag(c,"IsSoftDelete");
        row["CreatedAtUtc"] = c["CreatedAtUtc"]!.DeepClone(); row["UpdatedAtUtc"] = c["UpdatedAtUtc"]?.DeepClone();
        return row;
    }
    private static JsonObject Content(JsonObject row)
    {
        var result = new JsonObject();
        foreach (var k in new[] { "DrinkSlug", "CustomDrinkName", "DrinkNameRu", "DrinkNameEn", "Rating_Place", "Rating_Service", "Rating_Coffee" })
            result[k] = row[k]?.DeepClone();
        result["Rating_AverageRating"] = (Int(row,"Rating_Place") + Int(row,"Rating_Service") + Int(row,"Rating_Coffee")) / 3m;
        return result;
    }
    private static string ContentHash(JsonObject r)
    { var c = Content(r); c["Note"] = Text(r,"Comment") ?? Text(r,"Note"); return Hash(c); }
    private static string PhotoKeys(JsonObject inventory, string table, string column, string id) =>
        string.Join("|", Rows(inventory,table).Where(p => Text(p,column) == id).Select(p => Text(p,"StorageKey")).Order());
        private static string PublishedPhotoKeys(JsonObject inventory,string reviewId,JsonObject? visit)
    {
        var visitId = visit == null ? null : Text(visit,"Id");
        return string.Join("|",Rows(inventory,"ShopPhotos").Where(p => Text(p,"ReviewId") == reviewId
            || (visitId != null && Text(p,"CheckInId") == visitId)).Select(p => Text(p,"StorageKey")).Distinct().Order());
    }
    public static JsonObject Outcome(string table, string id, string outcome, string? reason) =>
        new() { ["SourceTable"] = table, ["SourceId"] = id, ["Outcome"] = outcome, ["Reason"] = reason };
    public static JsonObject Stage(JsonObject target)
    {
        var row = (JsonObject)target.DeepClone();
        row["Visibility"] = 0; row["ModerationState"] = 0; row["ApprovedRevision"] = null; row["FirstPublishedAtUtc"] = null;
        return row;
    }
    public static string Hash(JsonNode? node) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(node)?.ToJsonString() ?? "null")));
    private static JsonNode? Normalize(JsonNode? node)
    {
        if (node is JsonObject o)
        {
            var r = new JsonObject();
            foreach (var (key,value) in o.OrderBy(x => x.Key, StringComparer.Ordinal)) r[key] = Normalize(value);
            return r;
        }
        if (node is JsonArray a) return new JsonArray(a.Select(Normalize).ToArray());
        // Authored strings are provenance, even when their text resembles a number, UUID or date.
        if (node?.GetValueKind() == JsonValueKind.Number
            && decimal.TryParse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return JsonNode.Parse(number.ToString("G29", CultureInfo.InvariantCulture));
        return node?.DeepClone();
    }
    public static bool Matches(JsonObject expected, JsonObject actual) =>
        expected.All(kv => actual.TryGetPropertyValue(kv.Key, out var value) && MatchesColumn(kv.Key, kv.Value, value));
    private static bool MatchesColumn(string column, JsonNode? expected, JsonNode? actual)
    {
        if (expected == null || actual == null) return expected == null && actual == null;
        if (expected.GetValueKind() == JsonValueKind.String && actual.GetValueKind() == JsonValueKind.String)
        {
            if (column is "Id" or "UserId" or "ShopId" or "OwnerId" or "CheckInId" or "ModeratedBy"
                or "ModerationSubmissionId" or "RowVersion" or "CanonicalCheckInId" or "ModerationCheckInId"
                or "CheckInPhotoId" or "ModerationReviewId" or "ReviewId" or "CoffeeShopId" or "ModerationShopId")
                return Guid.TryParse(expected.ToString(), out var expectedId) && Guid.TryParse(actual.ToString(), out var actualId)
                    && expectedId == actualId;
            if (column is "CreatedAtUtc" or "UpdatedAtUtc" or "VisitedAt" or "VisitedAtUtc" or "DeletedAtUtc"
                or "FirstPublishedAtUtc" or "ModeratedAt" or "ClaimedAtUtc" or "UploadedAt" or "PermalinkExpiresAt"
                or "ScheduledDeletionAt" or "DeletedAt" or "ResolvedAtUtc")
                return DateTimeOffset.TryParse(expected.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expectedDate)
                    && DateTimeOffset.TryParse(actual.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var actualDate)
                    && expectedDate == actualDate;
        }
        return Hash(expected) == Hash(actual);
    }
    public static NpgsqlCommand InsertCommand(string table, JsonObject row, NpgsqlConnection? connection = null, NpgsqlTransaction? transaction = null)
    {
        if (!new[] { "CanonicalCheckIns", "ModerationCheckIns", "CheckInHelpfulVotes", "ShopPhotos" }.Contains(table))
            throw new ArgumentException("Not a migration target.", nameof(table));
        if (row.Any(kv => !System.Text.RegularExpressions.Regex.IsMatch(kv.Key, "\\A[A-Za-z][A-Za-z0-9_]*\\z")))
            throw new ArgumentException("Invalid column.");
        var columns = string.Join(", ", row.Select(k => "\""+k.Key+"\""));
        var command = new NpgsqlCommand($"INSERT INTO \"{table}\" ({columns}) SELECT {columns} FROM jsonb_populate_record(NULL::\"{table}\", @row) ON CONFLICT DO NOTHING", connection, transaction);
        command.Parameters.Add("row", NpgsqlDbType.Jsonb).Value = row.ToJsonString();
        return command;
    }
}