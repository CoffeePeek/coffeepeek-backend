using System.Net;
using System.Text.Json;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.Menu;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.Extensions.Options;
using DomainAvailability = CoffeePeek.Shops.Domain.Aggregates.MenuAggregate.MenuItemAvailability;
using DomainPriceRange = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.PriceRange;

namespace CoffeePeek.Shops.Application.Features.Admin.Menu;

public static class RemoveAdminShopMenuPhotosHandler
{
    public static async Task<Response<AdminShopMenuDto>> Handle(
        RemoveAdminShopMenuPhotosCommand command,
        ICoffeeShopRepository shops,
        IShopMenuRepository menuRepository,
        IQueryShopMenuRepository queryMenu,
        IQueryCoffeeDrinkRepository drinks,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = await shops.GetByIdAsync(command.ShopId, ct);
        if (shop is null)
            return Response<AdminShopMenuDto>.Error(HttpStatusCode.NotFound, "Shop not found.");

        var menu = await menuRepository.GetTrackedByShopIdAsync(command.ShopId, ct);
        if (menu is null)
            return Response<AdminShopMenuDto>.Error(HttpStatusCode.NotFound, "Menu not found.");

        var removed = menu.RemovePhotos(command.PhotoIds ?? []);
        if (removed.IsFailed)
            return Response<AdminShopMenuDto>.Error(HttpStatusCode.BadRequest, removed.Errors[0].Message);

        await unitOfWork.SaveChangesAsync(ct);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(shop.Id));
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);

        var catalog = await drinks.GetActiveAsync(ct);
        var dto = ShopMenuDtoFactory.FromShopMenu(
            await queryMenu.GetByShopIdAsync(command.ShopId, ct), catalog, mediaOptions.Value)!;
        return Response<AdminShopMenuDto>.Success(new AdminShopMenuDto(dto, []));
    }
}
