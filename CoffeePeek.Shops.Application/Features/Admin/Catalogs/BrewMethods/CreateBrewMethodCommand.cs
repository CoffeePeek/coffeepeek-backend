using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.BrewMethods;

public record CreateBrewMethodCommand(string Name, BrewMethodCategoryEnum Category);
