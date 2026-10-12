using CoffeePeek.Contract.Dtos.Menu;

namespace CoffeePeek.Shops.Application.Features.Catalogs.GetMenuDrinks;

public record GetMenuDrinksResponse(CoffeeDrinkDefinitionDto[] Drinks);
