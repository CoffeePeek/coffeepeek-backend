using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.Extensions.Options;
using Wolverine;

namespace CoffeePeek.Shops.Application.Features.Admin.Shops;

public static class SetAdminCoffeeShopVisibilityHandler
{
    public static async Task<Response<AdminPublishedShopDto>> Handle(
        SetAdminCoffeeShopVisibilityCommand command,
        ICoffeeShopRepository repository,
        IMessageBus bus,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = await repository.GetByIdAsync(command.ShopId, ct);
        if (shop is null)
            return Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Shop not found.");

        if (shop.IsHidden != command.Hidden)
        {
            shop.SetHidden(command.Hidden);
            shop.AdvanceProfileRevision();
            await bus.PublishAsync(new ShopProfileCacheInvalidationRequested(shop.Id));
        }

        return Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }
}
