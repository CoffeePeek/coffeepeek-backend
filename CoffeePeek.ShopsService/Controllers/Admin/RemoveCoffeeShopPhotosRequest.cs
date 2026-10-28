namespace CoffeePeek.ShopsService.Controllers.Admin;

public record RemoveCoffeeShopPhotosRequest(IReadOnlyList<Guid> PhotoIds);
