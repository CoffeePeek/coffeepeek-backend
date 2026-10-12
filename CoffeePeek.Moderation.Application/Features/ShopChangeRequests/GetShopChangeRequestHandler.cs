using System.Net;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Moderation.Application.Features.ShopChangeRequests;

public static class GetShopChangeRequestHandler
{
    public static async Task<Response<ShopChangeRequestDto>> Handle(
        GetShopChangeRequestQuery query,
        IShopChangeRequestRepository repository,
        CancellationToken ct)
    {
        var request = query.IsModerator
            ? await repository.GetByIdAsync(query.RequestId, ct)
            : await repository.GetByIdForUserAsync(query.RequestId, query.UserId, ct);
        return request is null
            ? Response<ShopChangeRequestDto>.Error(HttpStatusCode.NotFound, "Change request not found.")
            : Response<ShopChangeRequestDto>.Success(ShopChangeRequestPayload.Map(request));
    }
}
