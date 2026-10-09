using System.Text.Json.Serialization;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.BrewMethods;

public record UpdateBrewMethodCommand([property: JsonIgnore] Guid Id, string Name, BrewMethodCategoryEnum Category);
