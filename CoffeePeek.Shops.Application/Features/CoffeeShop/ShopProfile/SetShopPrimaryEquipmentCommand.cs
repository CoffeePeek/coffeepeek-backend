namespace CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;

public sealed record SetShopPrimaryEquipmentCommand(
    Guid ShopId,
    Guid? OwnerUserId,
    IReadOnlyList<Guid> PrimaryEquipmentIds);
