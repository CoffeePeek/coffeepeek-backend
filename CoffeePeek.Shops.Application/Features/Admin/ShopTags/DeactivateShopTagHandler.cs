using System.Net;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;

namespace CoffeePeek.Shops.Application.Features.Admin.ShopTags;

public static class DeactivateShopTagHandler
{
    public static async Task<Response<AdminShopTagDto>> Handle(
        DeactivateShopTagCommand command,
        IShopTagRepository repository,
        CancellationToken ct)
    {
        var tag = await repository.GetByIdAsync(command.Id, ct);
        if (tag is null)
            return Response<AdminShopTagDto>.Error(HttpStatusCode.NotFound, "Tag not found.");

        tag.Deactivate();

        return Response<AdminShopTagDto>.Success(AdminShopTagMapper.Map(tag));
    }
}
