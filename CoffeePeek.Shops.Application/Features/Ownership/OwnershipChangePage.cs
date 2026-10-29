namespace CoffeePeek.Shops.Application.Features.Ownership;

public record OwnershipChangePage(OwnershipChangeDto[] Items, int TotalCount, int Page, int PageSize);
