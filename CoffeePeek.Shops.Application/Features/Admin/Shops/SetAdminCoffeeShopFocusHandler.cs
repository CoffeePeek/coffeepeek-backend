using System.Net;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.Extensions.Options;
using Wolverine;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;

namespace CoffeePeek.Shops.Application.Features.Admin.Shops;

public static class SetAdminCoffeeShopFocusHandler
{
    public static async Task<Response<AdminPublishedShopDto>> Handle(
        SetAdminCoffeeShopFocusCommand command,
        ICoffeeShopRepository repository,
        IMessageBus bus,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = await repository.GetByIdAsync(command.ShopId, ct);
        if (shop is null)
            return Response<AdminPublishedShopDto>.Error(HttpStatusCode.NotFound, "Shop not found.");

        var focus = (CoffeeFocusType)(int)command.Type;
        var validation = DomainCoffeeShop.ValidateCoffeeFocus(focus);
        if (validation.IsFailed)
            return Response<AdminPublishedShopDto>.Error(HttpStatusCode.BadRequest, validation.Errors[0].Message);

        if (shop.Type != focus)
        {
            shop.SetCoffeeFocus(focus);
            shop.AdvanceProfileRevision();
            await bus.PublishAsync(new ShopProfileCacheInvalidationRequested(shop.Id));
        }

        return Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }
}
