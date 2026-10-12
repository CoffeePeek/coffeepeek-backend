using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Contract.Events.Shops;

namespace CoffeePeek.Shops.Application.Services;

public interface IApplyApprovedShopMenuChangeService
{
    Task<ShopChangeRequestApplyFailedV1?> ApplyAsync(ShopChangeRequestApprovedV1 message, CancellationToken ct);
}
