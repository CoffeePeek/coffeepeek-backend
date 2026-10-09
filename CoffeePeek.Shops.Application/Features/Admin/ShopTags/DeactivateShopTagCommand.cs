using System.Text.Json.Serialization;

namespace CoffeePeek.Shops.Application.Features.Admin.ShopTags;

public record DeactivateShopTagCommand([property: JsonIgnore] Guid Id);
