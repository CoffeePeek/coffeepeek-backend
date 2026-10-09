using System.Text.Json.Serialization;

namespace CoffeePeek.Shops.Application.Features.Admin.Catalogs.Beans;

public record DeleteCoffeeBeanCommand([property: JsonIgnore] Guid Id);
