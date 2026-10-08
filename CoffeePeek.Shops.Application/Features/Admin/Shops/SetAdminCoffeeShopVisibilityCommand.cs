namespace CoffeePeek.Shops.Application.Features.Admin.Shops;

public record SetAdminCoffeeShopVisibilityCommand(Guid ShopId, bool Hidden);
