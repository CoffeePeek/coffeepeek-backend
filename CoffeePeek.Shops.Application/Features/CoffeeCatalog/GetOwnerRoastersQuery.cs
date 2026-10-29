namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public record GetOwnerRoastersQuery(Guid OwnerUserId, int Page = 1, int PageSize = 20);
