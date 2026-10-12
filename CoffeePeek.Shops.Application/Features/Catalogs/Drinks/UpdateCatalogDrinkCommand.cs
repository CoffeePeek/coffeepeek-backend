using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

namespace CoffeePeek.Shops.Application.Features.Catalogs.Drinks;

public record UpdateCatalogDrinkCommand(string Slug, string NameRu, string NameEn,
    CoffeeDrinkCategory Category, string Aliases, int SortOrder, bool IsActive);
