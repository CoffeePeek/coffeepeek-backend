using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Application.Features.Admin.Audit;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using DomainSection = CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate.ShopChangeSection;
using DomainStatus = CoffeePeek.Moderation.Domain.Common.Enums.ModerationStatus;
using ShopChangeSection = CoffeePeek.Contract.Enums.ShopChangeSection;

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
