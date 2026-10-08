using System.Net;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Application.Features.Admin.Shops;

public static class GetAdminCoffeeShopByIdHandler
{
    public static async Task<Response<AdminPublishedShopDto>> Handle(
        GetAdminCoffeeShopByIdQuery query,
        ICoffeeShopRepository repository,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = await repository.GetByIdWithCatalogsAsync(query.ShopId, ct);
        return shop is null
            ? Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Shop not found.")
            : Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }
}
