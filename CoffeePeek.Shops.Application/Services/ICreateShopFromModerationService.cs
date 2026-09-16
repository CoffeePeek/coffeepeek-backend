using CoffeePeek.Contract.Dtos.CoffeeShop;

namespace CoffeePeek.Shops.Application.Services;

public interface ICreateShopFromModerationService
{
    Task<Guid> CreateShopFromApprovedEventAsync(ShopDto shopDto, Guid? creatorId, Guid moderationId, CancellationToken cancellationToken = default);
}
