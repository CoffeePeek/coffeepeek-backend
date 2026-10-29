namespace CoffeePeek.Contract.Dtos.Shop;

public record OwnerRoastersPage(RoasterDto[] Items, int TotalCount, int Page, int PageSize);
