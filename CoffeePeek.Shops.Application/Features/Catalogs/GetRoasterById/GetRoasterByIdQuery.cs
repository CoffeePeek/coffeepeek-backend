namespace CoffeePeek.Shops.Application.Features.Catalogs.GetRoasterById;

public record GetRoasterByIdQuery(Guid Id, Guid? OwnerUserId = null);
