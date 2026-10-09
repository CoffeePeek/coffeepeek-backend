using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.Extensions.Options;
using Wolverine;

namespace CoffeePeek.Shops.Application.Features.Admin.Shops.ReorderPhotos;

public static class ReorderAdminCoffeeShopPhotosHandler
{
    public static async Task<Response<AdminPublishedShopDto>> Handle(
        ReorderAdminCoffeeShopPhotosCommand command,
        ICoffeeShopRepository repository,
        IMessageBus bus,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = await repository.GetByIdAsync(command.ShopId, ct);
        if (shop is null)
            return Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Shop not found.");

        var previousOrder = shop.ShopPhotos.OrderBy(photo => photo.SortIndex).Select(photo => photo.Id).ToArray();
        var reorder = shop.ReorderPhotos(command.PhotoIds);
        if (reorder.IsFailed)
            return Response<AdminPublishedShopDto>.Error(
                System.Net.HttpStatusCode.BadRequest,
                reorder.Errors[0].Message);

        if (!previousOrder.SequenceEqual(command.PhotoIds))
        {
            shop.AdvanceProfileRevision();
            await bus.PublishAsync(new ShopProfileCacheInvalidationRequested(shop.Id));
        }

        return Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }
}
