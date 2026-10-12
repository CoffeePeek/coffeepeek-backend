using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

namespace CoffeePeek.Shops.Application.Features.Catalogs.Drinks;

public record AdminDrinkDto(Guid Id, string Slug, string NameRu, string NameEn,
    CoffeeDrinkCategory Category, string Aliases, int SortOrder, bool IsActive)
{
    public static AdminDrinkDto From(CoffeeDrinkDefinition drink) => new(drink.Id, drink.Slug,
        drink.NameRu, drink.NameEn, drink.Category, drink.Aliases, drink.SortOrder, drink.IsActive);
}
