using System.Net;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using DomainSection = CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate.ShopChangeSection;

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
