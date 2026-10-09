using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.ShopTags;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;

namespace CoffeePeek.Shops.Application.Features.Catalogs.GetShopTags;

public static class GetShopTagsHandler
{
    public static async Task<Response<GetShopTagsResponse>> Handle(
        GetShopTagsCommand command,
        IQueryShopTagRepository repository,
        CancellationToken ct)
    {
        var active = await repository.GetActiveAsync(ct);
        var tags = active.Select(AdminShopTagMapper.ToPublicDto).ToArray();
        return Response<GetShopTagsResponse>.Success(new GetShopTagsResponse(tags));
    }
}
