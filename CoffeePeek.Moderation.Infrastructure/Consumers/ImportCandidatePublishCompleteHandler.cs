using CoffeePeek.Contract.Responses;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Shared.Kernel;

namespace CoffeePeek.Moderation.Infrastructure.Consumers;

public static class ImportCandidatePublishCompleteHandler
{
    public static async Task Handle(
        ImportCandidatePublishCompleteResponse message,
        IShopImportCandidateRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        // One query for the whole batch (was one round trip per result). An invalid result is skipped rather than
        // thrown, so a single bad item cannot fail and endlessly retry the rest of the batch.
        var candidates = await repository.GetByIdsAsync(
            message.Results.Select(r => r.CandidateId).Distinct().ToList(), ct);

        foreach (var result in message.Results)
        {
            if (result.ShopId == Guid.Empty || !candidates.TryGetValue(result.CandidateId, out var candidate)
                || candidate.ResultingShopId == result.ShopId)
                continue;

            candidate.AttachPublishedShop(result.ShopId);
        }

        await unitOfWork.SaveChangesAsync(ct);
    }
}
