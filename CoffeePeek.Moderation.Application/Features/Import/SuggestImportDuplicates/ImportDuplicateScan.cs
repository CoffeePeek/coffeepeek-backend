using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Moderation.Domain.Import;

namespace CoffeePeek.Moderation.Application.Features.Import.SuggestImportDuplicates;

public static class ImportDuplicateScan
{
    public static async Task<int> AddNewAsync(
        IReadOnlyList<ShopImportCandidate> candidates,
        IShopImportDuplicateSuggestionRepository suggestions,
        CancellationToken ct)
    {
        var existingPairs = await suggestions.ListPairKeysAsync(ct);
        var created = ImportDuplicateScanner.Scan(candidates, existingPairs);
        if (created.Count > 0)
            suggestions.AddRange(created);

        return created.Count;
    }
}
