using CoffeePeek.Shops.Application.Features.Admin.Shops;

namespace CoffeePeek.Shops.Application.Features.Owner;

public record GetOwnerCoffeeShopsResponse(IReadOnlyList<AdminPublishedShopDto> Items);
