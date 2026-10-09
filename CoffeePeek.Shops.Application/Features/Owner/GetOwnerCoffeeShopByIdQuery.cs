namespace CoffeePeek.Shops.Application.Features.Owner;

public record GetOwnerCoffeeShopByIdQuery(Guid ShopId, Guid OwnerUserId);
