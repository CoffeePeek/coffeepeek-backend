using CoffeePeek.Contract.Responses;
using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Moderation.Application.Features.Menu;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Shared.Kernel;

namespace CoffeePeek.Moderation.Infrastructure.Consumers;

public static class ImportCandidatePublishCompleteHandler
{
    public static async Task<ApplyShopMenuSnapshotEvent[]> Handle(
        ImportCandidatePublishCompleteResponse message,
        IShopImportCandidateRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var outbound = new List<ApplyShopMenuSnapshotEvent>();
        var candidates = await repository.GetByIdsAsync(
            message.Results.Select(r => r.CandidateId).Distinct().ToList(), ct);

        foreach (var result in message.Results)
        {
            if (result.ShopId == Guid.Empty || !candidates.TryGetValue(result.CandidateId, out var candidate)
                || candidate.ResultingShopId == result.ShopId)
                continue;

            candidate.AttachPublishedShop(result.ShopId);
            if (MenuDraftMapper.ToSnapshot(candidate.Menu) is { } snapshot)
                outbound.Add(new ApplyShopMenuSnapshotEvent(result.ShopId, snapshot, false, null));
        }

        await unitOfWork.SaveChangesAsync(ct);
        return outbound.ToArray();
    }
}
