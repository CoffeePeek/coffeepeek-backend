using System.Text.Json.Serialization;

namespace CoffeePeek.Shops.Application.Features.Admin.ShopTags;

public record UpdateShopTagCommand(
    [property: JsonIgnore] Guid Id,
    string Name,
    string? Description,
    int SortOrder,
    bool IsActive);
