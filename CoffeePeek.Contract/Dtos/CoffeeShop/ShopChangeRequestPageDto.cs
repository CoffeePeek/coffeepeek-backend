namespace CoffeePeek.Contract.Dtos.CoffeeShop;

public record ShopChangeRequestPageDto(
    IReadOnlyList<ShopChangeRequestDto> Items,
    int TotalItems,
    int TotalPages,
    int CurrentPage,
    int PageSize);
