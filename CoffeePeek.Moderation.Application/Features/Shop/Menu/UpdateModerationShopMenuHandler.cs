using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Moderation.Application.Features.Menu;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using MapsterMapper;
using DomainPriceRange = CoffeePeek.Moderation.Domain.Aggregates.Enums.PriceRange;

namespace CoffeePeek.Moderation.Application.Features.Shop.Menu;

public static class UpdateModerationShopMenuHandler
{
    public static async Task<(Response<ModerationShopDto>, ApplyShopMenuSnapshotEvent?)> Handle(
        UpdateModerationShopMenuCommand command,
        IModerationShopRepository repository,
        IUnitOfWork unitOfWork,
        IMapper mapper,
        CancellationToken ct)
    {
        var shop = await repository.GetByIdAsync(command.ShopId, ct);
        if (shop is null)
            return (Response<ModerationShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Coffee shop not found"), null);

        shop.ReplaceMenuItems(MenuDraftMapper.ToDraftItems(command.Items), DateTime.UtcNow);

        if (command.ApplySuggestedPriceRange && shop.Menu?.SuggestedPriceRange is { } range)
            shop.ApplySuggestedPriceRange((DomainPriceRange)range);

        await unitOfWork.SaveChangesAsync(ct);

        ApplyShopMenuSnapshotEvent? outbound = null;
        if (shop.ShopId != Guid.Empty && MenuDraftMapper.ToSnapshot(shop.Menu) is { } snapshot)
            outbound = new ApplyShopMenuSnapshotEvent(shop.ShopId, snapshot, command.ApplySuggestedPriceRange, command.UserId);

        return (Response<ModerationShopDto>.Success(mapper.Map<ModerationShopDto>(shop)), outbound);
    }
}
