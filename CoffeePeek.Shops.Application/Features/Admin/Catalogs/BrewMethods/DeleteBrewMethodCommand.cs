using System.Text.Json.Serialization;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.BrewMethods;

public record DeleteBrewMethodCommand([property: JsonIgnore] Guid Id);
