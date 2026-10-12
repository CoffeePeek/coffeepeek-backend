namespace CoffeePeek.Shops.Application.Features.Admin.Shops;

public record AssignCoffeeShopOwnerCommand(Guid ShopId, Guid? OwnerUserId);
