using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Moderation.Application.Features.Menu;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using MapsterMapper;

namespace CoffeePeek.Moderation.Application.Features.Shop.Menu;

public static class ParseModerationShopMenuHandler
{
    public static async Task<(Response<ModerationShopDto>, ParseMenuRequestedEvent?)> Handle(
        ParseModerationShopMenuCommand command,
        IModerationShopRepository repository,
        IUnitOfWork unitOfWork,
        IMapper mapper,
        CancellationToken ct)
    {
        var shop = await repository.GetByIdAsync(command.ShopId, ct);
        if (shop is null)
            return (Response<ModerationShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Coffee shop not found"), null);

        try
        {
            shop.RequestMenuParse();
        }
        catch (DomainException ex)
        {
            return (Response<ModerationShopDto>.Error(System.Net.HttpStatusCode.BadRequest, ex.Message), null);
        }

        await unitOfWork.SaveChangesAsync(ct);

        return (
            Response<ModerationShopDto>.Success(mapper.Map<ModerationShopDto>(shop)),
            MenuParseEvents.FromDraft(
                MenuParseSourceKind.ModerationShop,
                shop.Id,
                shop.ShopId == Guid.Empty ? null : shop.ShopId,
                shop.Menu,
                command.UserId));
    }
}
