namespace CoffeePeek.ShopsService.Controllers.Admin;

public record UpdateShopCatalogsRequest(
    IReadOnlyList<Guid>? EquipmentIds,
    IReadOnlyList<Guid>? BeanIds,
    IReadOnlyList<Guid>? RoasterIds,
    IReadOnlyList<Guid>? BrewMethodIds);
