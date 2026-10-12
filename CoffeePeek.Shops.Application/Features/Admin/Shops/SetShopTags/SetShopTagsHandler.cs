using System.Net;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using Wolverine;

namespace CoffeePeek.Shops.Application.Features.Admin.Shops.SetShopTags;

public static class SetShopTagsHandler
{
    public static async Task<Response> Handle(
        SetShopTagsCommand command,
        ICoffeeShopRepository shopRepository,
        IQueryShopTagRepository tagRepository,
        IMessageBus bus,
        CancellationToken ct)
    {
        var shop = await shopRepository.GetByIdAsync(command.ShopId, ct);
        if (shop is null)
            return Response.Error((int)HttpStatusCode.NotFound, "Shop not found.");

        var tagIds = command.TagIds ?? [];
        if (!await tagRepository.AllExistAndActiveAsync(tagIds, ct))
            return Response.Error((int)HttpStatusCode.BadRequest, "One or more tags do not exist or are inactive.");

        try
        {
            shop.SetTags(tagIds, command.AdminUserId);
            shop.AdvanceProfileRevision();
        }
        catch (Exception ex) when (ex is ArgumentException or Shared.Kernel.Exceptions.DomainException)
        {
            return Response.Error((int)HttpStatusCode.BadRequest, ex.Message);
        }

        await bus.PublishAsync(new ShopProfileCacheInvalidationRequested(shop.Id));

        return Response.Success("Shop tags updated.");
    }
}
