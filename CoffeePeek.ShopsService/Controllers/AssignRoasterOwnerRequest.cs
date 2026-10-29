namespace CoffeePeek.ShopsService.Controllers;

public record AssignRoasterOwnerRequest(Guid? OwnerUserId, string? Reason = null);
