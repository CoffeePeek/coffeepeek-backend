using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Shops.Application.Features.Admin.Shops;

public record SetAdminCoffeeShopFocusCommand(Guid ShopId, CoffeeShopType Type, Guid AdminUserId);
