using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;

namespace CoffeePeek.Shops.Application.Features.Admin.ShopTags;

public static class GetAllAdminShopTagsHandler
{
    public static async Task<Response<AdminShopTagDto[]>> Handle(
        GetAllAdminShopTagsQuery query,
        IQueryShopTagRepository repository,
        CancellationToken ct)
    {
        var tags = await repository.GetAllAsync(ct);
        return Response<AdminShopTagDto[]>.Success(tags.Select(AdminShopTagMapper.Map).ToArray());
    }
}
