using System.Text.Json.Serialization;

namespace CoffeePeek.Shops.Application.Features.Admin.Shops.SetShopTags;

public record SetShopTagsCommand(
    [property: JsonIgnore] Guid ShopId,
    Guid[] TagIds,
    [property: JsonIgnore] Guid AdminUserId);
