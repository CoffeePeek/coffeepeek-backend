using System.Text.Json.Serialization;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Cities;

public record UpdateCityCommand([property: JsonIgnore] Guid Id, string Name);
