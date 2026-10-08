using CoffeePeek.Contract.Dtos;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Contract.Dtos.ShopPhotos;
using CoffeePeek.Contract.Events;
using Wolverine;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using Microsoft.Extensions.Options;
using DomainCoffeeShop = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop;

namespace CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;

/// <summary>Deletes gallery photos on a published shop and returns the updated shop.</summary>
public static class RemovePublishedShopPhotosHandler
{
    public static async Task<Response<AdminPublishedShopDto>> Handle(
        RemovePublishedShopPhotosCommand command,
        ICoffeeShopRepository repository,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = await AddPublishedShopPhotosHandler.LoadShopAsync(
            repository, command.ShopId, command.OwnerUserId, ct);
        if (shop is null)
            return Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Shop not found.");

        var removed = shop.RemovePhotos(command.PhotoIds ?? []);
        if (removed.IsFailed)
            return Response<AdminPublishedShopDto>.Error(
                System.Net.HttpStatusCode.BadRequest,
                removed.Errors[0].Message);

        shop.AdvanceProfileRevision();

        await unitOfWork.SaveChangesAsync(ct);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(shop.Id));
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);

        return Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }
}
