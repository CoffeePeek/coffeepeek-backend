namespace CoffeePeek.Shops.Application.Features.Ownership;

public record OwnershipClaimPage(OwnershipClaimDto[] Items, int TotalCount, int Page, int PageSize);
