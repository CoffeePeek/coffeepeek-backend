namespace CoffeePeek.ShopsService.Controllers.Admin;

public record AssignCoffeeShopOwnerRequest(Guid? OwnerUserId, string? Reason = null);
