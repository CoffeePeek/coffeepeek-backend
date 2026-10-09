
namespace CoffeePeek.Shops.Application.Features.Admin.ShopTags;

public record AdminShopTagDto(
    Guid Id,
    string Slug,
    string Name,
    string? Description,
    int SortOrder,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
