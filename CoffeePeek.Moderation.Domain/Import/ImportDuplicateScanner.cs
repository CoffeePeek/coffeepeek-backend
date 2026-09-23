using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Shared.Domain.Places;

namespace CoffeePeek.Moderation.Domain.Import;

public static class ImportDuplicateScanner
{
    private const decimal LatitudeWindow = 0.004m;
    private const decimal LongitudeWindow = 0.007m;

    public static IReadOnlyList<ShopImportDuplicateSuggestion> Scan(
        IReadOnlyList<ShopImportCandidate> candidates,
        IReadOnlySet<(Guid Left, Guid Right)> existingPairs)
    {
        // Sorted sweep: once the latitude gap exceeds the window no later candidate can match, so each candidate is
        // only compared with its latitude neighbours instead of every other candidate (was O(n²) over the table).
        var eligible = candidates
            .Where(c => c.QueueStatus is not ImportQueueStatus.Rejected)
            .OrderBy(c => c.Latitude)
            .ToList();

        var suggestions = new List<ShopImportDuplicateSuggestion>();
        for (var i = 0; i < eligible.Count; i++)
        {
            var left = eligible[i];
            for (var j = i + 1; j < eligible.Count; j++)
            {
                var right = eligible[j];
                if (right.Latitude - left.Latitude > LatitudeWindow)
                    break;
                if (Math.Abs(left.Longitude - right.Longitude) > LongitudeWindow)
                    continue;

                var pair = ShopImportDuplicateSuggestion.Order(left.Id, right.Id);
                if (existingPairs.Contains(pair))
                    continue;

                var hint = PlaceDuplicateSuggester.Evaluate(
                    left.Name,
                    left.Address,
                    left.Latitude,
                    left.Longitude,
                    right.Name,
                    right.Address,
                    right.Latitude,
                    right.Longitude,
                    left.Phone,
                    right.Phone,
                    left.Instagram,
                    right.Instagram,
                    left.Brand,
                    right.Brand);

                if (hint is null)
                    continue;

                suggestions.Add(ShopImportDuplicateSuggestion.Create(left.Id, right.Id, hint.Value));
            }
        }

        return suggestions;
    }

    public static ShopImportCandidate PickKeeper(ShopImportCandidate a, ShopImportCandidate b)
    {
        var scoreA = KeeperScore(a);
        var scoreB = KeeperScore(b);
        if (scoreA != scoreB)
            return scoreA > scoreB ? a : b;

        return a.CreatedAtUtc <= b.CreatedAtUtc ? a : b;
    }

    private static int KeeperScore(ShopImportCandidate candidate)
    {
        var score = 0;
        // A candidate linked to a catalog shop must survive the merge, otherwise that shop is orphaned.
        if (candidate.ResultingShopId is not null)
            score += 2000;
        if (candidate.QueueStatus == ImportQueueStatus.Published)
            score += 1000;
        if (candidate.Source == ImportSource.Osm)
            score += 100;
        if (candidate.HasRealName)
            score += 10;
        if (!string.IsNullOrWhiteSpace(candidate.Phone))
            score += 5;
        if (!string.IsNullOrWhiteSpace(candidate.Instagram))
            score += 5;
        if (!string.IsNullOrWhiteSpace(candidate.Website))
            score += 3;
        if (!ShopPlaceMatcher.IsGenericAddress(candidate.Address))
            score += 2;
        return score;
    }
}
