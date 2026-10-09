
namespace CoffeePeek.Shops.Application.Features.Admin.ShopTags;

public record CreateShopTagCommand(
    string Slug,
    string Name,
    string? Description,
    int SortOrder = 0);
