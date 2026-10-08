namespace CoffeePeek.ShopsService.Controllers;

public sealed record SetShopPrimaryEquipmentRequest(IReadOnlyList<Guid> PrimaryEquipmentIds);
