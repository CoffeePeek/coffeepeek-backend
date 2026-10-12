namespace CoffeePeek.Shops.Application.Features.Admin.Shops;

public record GetAdminCoffeeShopsResponse(
    IReadOnlyList<AdminPublishedShopDto> Items,
    int TotalItems,
    int TotalPages,
    int CurrentPage,
    int PageSize);
