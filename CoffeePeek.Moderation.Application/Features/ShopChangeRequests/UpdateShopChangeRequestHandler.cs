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
public static class UpdateShopChangeRequestHandler
{
    public static async Task<Response<ShopChangeRequestDto>> Handle(
        UpdateShopChangeRequestCommand command,
        IShopChangeRequestRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var request = await repository.GetByIdAsync(command.RequestId, ct);
        if (request is null) return Response<ShopChangeRequestDto>.Error(HttpStatusCode.NotFound, "Change request not found.");

        string json;
        try { json = ShopChangeRequestPayload.Serialize(command.Section, command.Payload); }
        catch (ArgumentException ex) { return Response<ShopChangeRequestDto>.Error(HttpStatusCode.BadRequest, ex.Message); }

        request.Edit((DomainSection)command.Section, json);
        await unitOfWork.SaveChangesAsync(ct);
        return Response<ShopChangeRequestDto>.Success(ShopChangeRequestPayload.Map(request));
    }
}
