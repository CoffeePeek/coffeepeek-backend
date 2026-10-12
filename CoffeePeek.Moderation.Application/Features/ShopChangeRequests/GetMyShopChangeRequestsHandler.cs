using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Moderation.Application.Features.ShopChangeRequests;

public static class GetMyShopChangeRequestsHandler
{
    public static Task<Response<ShopChangeRequestPageDto>> Handle(
        GetMyShopChangeRequestsQuery query,
        IShopChangeRequestRepository repository,
        CancellationToken ct) => GetShopChangeRequestsHandler.GetPage(
        query.Page, query.PageSize, query.Status, query.ShopId, query.UserId, query.Section, repository, ct);
}
