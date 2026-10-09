using System.Net;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;

namespace CoffeePeek.Shops.Application.Features.Admin.ShopTags;

public static class UpdateShopTagHandler
{
    public static async Task<Response<AdminShopTagDto>> Handle(
        UpdateShopTagCommand command,
        IShopTagRepository repository,
        CancellationToken ct)
    {
        var tag = await repository.GetByIdAsync(command.Id, ct);
        if (tag is null)
            return Response<AdminShopTagDto>.Error(HttpStatusCode.NotFound, "Tag not found.");

        tag.Update(command.Name, command.Description, command.SortOrder, command.IsActive);

        return Response<AdminShopTagDto>.Success(AdminShopTagMapper.Map(tag));
    }
}
