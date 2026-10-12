using CoffeePeek.Contract.Dtos.Import;
using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Moderation.Application.Features.Menu;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Moderation.Application.Features.Import.Menu;

public static class UpdateImportCandidateMenuHandler
{
    public static async Task<(Response<ShopImportCandidateDto>, ApplyShopMenuSnapshotEvent?)> Handle(
        UpdateImportCandidateMenuCommand command,
        IShopImportCandidateRepository repository,
        IUnitOfWork unitOfWork,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var candidate = await repository.GetByIdAsync(command.CandidateId, ct);
        if (candidate is null)
            return (Response<ShopImportCandidateDto>.Error(System.Net.HttpStatusCode.NotFound, "Import candidate not found."), null);

        candidate.ReplaceMenuItems(MenuDraftMapper.ToDraftItems(command.Items), DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);

        ApplyShopMenuSnapshotEvent? outbound = null;
        if (candidate.ResultingShopId is { } shopId && MenuDraftMapper.ToSnapshot(candidate.Menu) is { } snapshot)
            outbound = new ApplyShopMenuSnapshotEvent(shopId, snapshot, command.ApplySuggestedPriceRange, command.UserId);

        return (
            Response<ShopImportCandidateDto>.Success(ShopImportCandidateMapper.ToDto(candidate, mediaOptions.Value)),
            outbound);
    }
}
