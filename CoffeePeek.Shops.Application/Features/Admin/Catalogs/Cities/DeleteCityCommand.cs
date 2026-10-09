using System.Text.Json.Serialization;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Cities;

public record DeleteCityCommand([property: JsonIgnore] Guid Id);
