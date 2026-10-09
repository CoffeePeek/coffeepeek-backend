using System.Text.Json.Serialization;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Beans;

public record UpdateCoffeeBeanCommand([property: JsonIgnore] Guid Id, string Name);
