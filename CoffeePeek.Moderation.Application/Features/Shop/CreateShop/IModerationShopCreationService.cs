namespace CoffeePeek.Moderation.Application.Features.Shop.CreateShop;

public interface IModerationShopCreationService
{
    Task<Guid> Create(
        SendCoffeeShopToModerationCommand command,
        CancellationToken cancellationToken);
}
