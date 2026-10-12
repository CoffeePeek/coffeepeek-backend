using CoffeePeek.Contract.Dtos.Import;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Moderation.Application.Features.Import.GetImportStats;

public static class GetImportStatsHandler
{
    public static async Task<Response<ImportStatsDto>> Handle(
        GetImportStatsQuery query,
        IShopImportCandidateRepository repository,
        IShopImportDuplicateSuggestionRepository suggestions,
        CancellationToken ct)
    {
        var stats = await repository.GetStatsAsync(ct);
        var pendingDuplicates = await suggestions.CountPendingAsync(ct);
        return Response<ImportStatsDto>.Success(new ImportStatsDto(
            stats.Pending,
            stats.Skipped,
            stats.Published,
            stats.Rejected,
            stats.InFeed,
            pendingDuplicates,
            stats.ByFocus.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            stats.ByBucket.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            new ImportRejectedByReasonDto(
                stats.RejectedByReason.Closed,
                stats.RejectedByReason.Invalid,
                stats.RejectedByReason.NotCoffee,
                stats.RejectedByReason.Duplicate,
                stats.RejectedByReason.Unspecified)));
    }
}
