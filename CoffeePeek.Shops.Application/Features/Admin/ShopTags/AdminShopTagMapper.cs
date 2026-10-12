using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;

namespace CoffeePeek.Shops.Application.Features.Admin.ShopTags;

public static class AdminShopTagMapper
{
    public static AdminShopTagDto Map(ShopTag tag) => new(
        tag.Id,
        tag.Slug,
        tag.Name,
        tag.Description,
        tag.SortOrder,
        tag.IsActive,
        tag.CreatedAtUtc,
        tag.UpdatedAtUtc);

    public static ShopTagDto ToPublicDto(ShopTag tag) => new(
        tag.Id,
        tag.Slug,
        tag.Name,
        tag.Description,
        tag.SortOrder);
}
