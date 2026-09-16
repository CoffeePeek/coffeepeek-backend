using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Application.Features.Owner.ReorderPhotos;

public static class ReorderOwnerCoffeeShopPhotosHandler
{
    public static async Task<Response<AdminPublishedShopDto>> Handle(
        ReorderOwnerCoffeeShopPhotosCommand command,
        ICoffeeShopRepository repository,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = await repository.GetByIdForOwnerAsync(command.ShopId, command.OwnerUserId, ct);
        if (shop is null)
            return Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Shop not found.");

        var reorder = shop.ReorderPhotos(command.PhotoIds);
        if (reorder.IsFailed)
            return Response<AdminPublishedShopDto>.Error(
                System.Net.HttpStatusCode.BadRequest,
                reorder.Errors[0].Message);

        await unitOfWork.SaveChangesAsync(ct);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(shop.Id));

        return Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }
}
