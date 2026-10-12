using System.Net;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using DomainSection = CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate.ShopChangeSection;

namespace CoffeePeek.Moderation.Application.Features.ShopChangeRequests;

public static class CreateShopChangeRequestHandler
{
    public static async Task<Response<ShopChangeRequestDto>> Handle(
        CreateShopChangeRequestCommand command,
        IShopChangeRequestRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        string json;
        try { json = ShopChangeRequestPayload.Serialize(command.Section, command.Payload); }
        catch (ArgumentException ex) { return Response<ShopChangeRequestDto>.Error(HttpStatusCode.BadRequest, ex.Message); }

        var request = ShopChangeRequest.Create(command.ShopId, command.UserId, (DomainSection)command.Section, json);
        repository.Add(request);
        await unitOfWork.SaveChangesAsync(ct);
        return Response<ShopChangeRequestDto>.Success(ShopChangeRequestPayload.Map(request));
    }
}
