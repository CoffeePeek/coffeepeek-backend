using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Application.Features.Owner;

public static class GetOwnerCoffeeShopsHandler
{
    public static async Task<Response<GetOwnerCoffeeShopsResponse>> Handle(
        GetOwnerCoffeeShopsQuery query,
        IAdminCoffeeShopQueryRepository repository,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var items = await repository.GetByOwnerUserIdAsync(query.OwnerUserId, ct);
        var media = mediaOptions.Value;
        var dtos = items.Select(s => AdminPublishedShopMapper.Map(s, media)).ToList();
        return Response<GetOwnerCoffeeShopsResponse>.Success(new GetOwnerCoffeeShopsResponse(dtos));
    }
}
