using System.Net;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Application.Features.Admin.Shops;

public static class AssignCoffeeShopOwnerHandler
{
    public static async Task<Response<AdminPublishedShopDto>> Handle(
        AssignCoffeeShopOwnerCommand command,
        ICoffeeShopRepository repository,
        IUnitOfWork unitOfWork,
        IOptions<MediaPublicUrlOptions> mediaOptions,
        CancellationToken ct)
    {
        var shop = await repository.GetByIdAsync(command.ShopId, ct);
        if (shop is null)
            return Response<AdminPublishedShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Shop not found.");

        shop.AssignOwner(command.OwnerUserId);
        shop.AdvanceProfileRevision();

        await unitOfWork.SaveChangesAsync(ct);

        return Response<AdminPublishedShopDto>.Success(AdminPublishedShopMapper.Map(shop, mediaOptions.Value));
    }
}
