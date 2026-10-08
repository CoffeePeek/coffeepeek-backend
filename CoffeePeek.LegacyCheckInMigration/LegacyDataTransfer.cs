using System.Text.Json.Nodes;
using CoffeePeek.MediaService.Services;

namespace CoffeePeek.LegacyCheckInMigration;

public static class LegacyDataTransfer
{
    public static async Task RunAsync(MigrationDatabase account, MigrationDatabase shops, MigrationDatabase moderation,
        MigrationDatabase media, IStorageService? storage)
    {
        var accounts = await account.RowsAsync("Users");
        var users = accounts.Where(x => !x.Flag("IsSoftDelete")).ToDictionary(x => x.Id(), x => x.Text("Username")!);
        var shopIds = (await shops.RowsAsync("Shops")).Select(x => x.Id()).ToHashSet();
        var visits = await shops.RowsAsync("CheckIns");
        var reviews = await shops.RowsAsync("Reviews");
        var snapshots = await moderation.RowsAsync("ModerationReviews");
        var existing = (await shops.RowsAsync("CanonicalCheckIns")).ToDictionary(x => x.Id());
        var plans = LegacyMigrationPlanner.Plan(visits, reviews, snapshots, users, shopIds);
        var accepted = new List<LegacyCheckIn>();
        foreach (var plan in plans)
        {
            if (existing.TryGetValue(plan.CheckIn.Id(), out var current) && current.Id("RowVersion") != plan.CheckIn.Id("RowVersion"))
                continue; // An existing canonical visit always wins; don't attach legacy data to it.
            await shops.InsertAsync("CanonicalCheckIns", plan.CheckIn);
            accepted.Add(plan);
        }

        await LegacyPhotoTransfer.RunAsync(shops, moderation, media, storage, accepted);
        foreach (var plan in accepted.Where(x => x.Snapshot != null && !x.Snapshot.Flag("IsSoftDelete")))
        {
            await moderation.InsertAsync("ModerationCheckIns", plan.Snapshot!);
            var saved = await moderation.ScalarAsync("""
                SELECT EXISTS (SELECT 1 FROM "ModerationCheckIns" WHERE "Id"=@id AND "CheckInId"=@visit AND "ContentRevision"=1)
                """, ("id", plan.ModerationId!.Value), ("visit", plan.CheckIn.Id()));
            if (!Equals(saved, true)) throw new InvalidOperationException("Conflicting canonical moderation snapshot.");
            foreach (var photo in (await shops.RowsAsync("ShopPhotos")).Where(x => x.Id("CanonicalCheckInId") == plan.CheckIn.Id()))
                await moderation.ExecuteAsync("UPDATE \"ShopPhotos\" SET \"ModerationCheckInId\"=@snapshot WHERE \"Id\"=@id",
                    ("snapshot", plan.ModerationId.Value), ("id", LegacyRow.StableId($"snapshot-photo:{plan.ModerationId}:{photo.Id()}")));
        }
        foreach (var plan in accepted.Where(x => x.Snapshot?.Flag("IsSoftDelete") == true))
        {
            foreach (var photo in (await shops.RowsAsync("ShopPhotos")).Where(x => x.Id("CanonicalCheckInId") == plan.CheckIn.Id()))
                await moderation.ExecuteAsync("DELETE FROM \"ShopPhotos\" WHERE \"Id\"=@id AND \"ModerationCheckInId\" IS NULL AND \"ModerationShopId\" IS NULL",
                    ("id", LegacyRow.StableId($"snapshot-photo:{plan.ModerationId}:{photo.Id()}")));
        }
        var reviewMap = accepted.Where(x => x.ReviewId != null).ToDictionary(x => x.ReviewId!.Value, x => x.CheckIn.Id());
        foreach (var vote in await shops.RowsAsync("ReviewHelpfulVotes"))
        {
            if (!reviewMap.TryGetValue(vote.Id("ReviewId"), out var visit) || !users.ContainsKey(vote.Id("UserId"))) continue;
            var author = accepted.First(x => x.CheckIn.Id() == visit).CheckIn.Id("UserId");
            if (author == vote.Id("UserId")) continue;
            var newVote = vote.Pick("UserId", "CreatedAtUtc");
            newVote["CheckInId"] = visit.ToString();
            await shops.InsertAsync("CheckInHelpfulVotes", newVote);
        }
        foreach (var report in await shops.RowsAsync("ReviewReports"))
        {
            var originalTarget = report.Id("ReviewId");
            var target = LegacyMigrationPlanner.ResolveReportTarget(originalTarget, reviewMap);
            if (target != originalTarget)
                await shops.ExecuteAsync("UPDATE \"ReviewReports\" SET \"ReviewId\"=@visit WHERE \"Id\"=@id", ("visit", target), ("id", report.Id()));
        }
        await shops.ExecuteAsync("""
            UPDATE "CanonicalCheckIns" c SET "HelpfulCount"=(SELECT count(*) FROM "CheckInHelpfulVotes" v WHERE v."CheckInId"=c."Id");
            """);
        // Account currently counts created canonical visits, including visits later soft-deleted.
        var counts = (await shops.RowsAsync("CanonicalCheckIns"))
            .GroupBy(x => x.Id("UserId")).ToDictionary(x => x.Key, x => x.Count());
        foreach (var user in accounts.Select(x => x.Id()))
            await account.ExecuteAsync("UPDATE \"Users\" SET \"CheckInCount\"=@count, \"StatisticUpdatedAtUtc\"=now() WHERE \"Id\"=@id",
                ("count", counts.GetValueOrDefault(user)), ("id", user));
        Console.WriteLine($"Legacy visits: {visits.Count}; reviews: {reviews.Count}; canonical records retained/transferred: {accepted.Count}; discarded source records: {visits.Count + reviews.Count - accepted.Sum(x => (x.VisitId != null ? 1 : 0) + (x.ReviewId != null ? 1 : 0))}.");
    }
}
