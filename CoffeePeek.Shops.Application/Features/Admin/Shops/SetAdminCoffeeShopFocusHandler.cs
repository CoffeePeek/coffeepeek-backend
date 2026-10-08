using System.Net;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.Extensions.Options;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;

namespace CoffeePeek.Shops.Application.Features.Admin.Shops;

public static class SetAdminCoffeeShopFocusHandler
{
    public static async Task<Response<AdminPublishedShopDto>> Handle(
        SetAdminCoffeeShopFocusCommand command,
        ICoffeeShopRepository repository,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
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

        shop.SetCoffeeFocus(focus);
        shop.AdvanceProfileRevision();

        await unitOfWork.SaveChangesAsync(ct);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(shop.Id));
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);

        return Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }
}
