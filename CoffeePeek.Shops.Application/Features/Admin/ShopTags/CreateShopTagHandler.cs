using System.Net;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;

namespace CoffeePeek.Shops.Application.Features.Admin.ShopTags;

public static class CreateShopTagHandler
{
    public static async Task<Response<AdminShopTagDto>> Handle(
        CreateShopTagCommand command,
        IShopTagRepository repository,
        CancellationToken ct)
    {
        var existing = await repository.GetBySlugAsync(command.Slug, ct);
        if (existing is not null)
            return Response<AdminShopTagDto>.Error(HttpStatusCode.Conflict, "A tag with this slug already exists.");

        var tag = ShopTag.Create(command.Slug, command.Name, command.Description, command.SortOrder);
        repository.Add(tag);

        return Response<AdminShopTagDto>.Success(AdminShopTagMapper.Map(tag));
    }

}
