using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Ownership;
using CoffeePeek.Shops.Domain.Aggregates.OwnershipAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Application.Features.Admin.Shops;

public static class AssignCoffeeShopOwnerHandler
{
    public static async Task<Response<AdminPublishedShopDto>> Handle(
        AssignCoffeeShopOwnerCommand command,
        ICoffeeShopRepository repository,
        BusinessOwnershipService ownership,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        try
        {
            await ownership.AssignAsync(BusinessType.CoffeeShop, command.ShopId, command.OwnerUserId,
                command.ActorUserId, command.Reason, false, null, ct);
        }
        catch (NotFoundException)
        {
            return Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Shop not found.");
        }
        var shop = await repository.GetByIdAsync(command.ShopId, ct);
        if (shop is null)
            return Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Shop not found.");

        return Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }
}
