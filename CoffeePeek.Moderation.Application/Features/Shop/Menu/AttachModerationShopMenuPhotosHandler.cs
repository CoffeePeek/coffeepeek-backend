using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Moderation.Application.Features.Menu;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;
using MapsterMapper;

namespace CoffeePeek.Moderation.Application.Features.Shop.Menu;

public static class AttachModerationShopMenuPhotosHandler
{
    public static async Task<(Response<ModerationShopDto>, ParseMenuRequestedEvent?)> Handle(
        AttachModerationShopMenuPhotosCommand command,
        IModerationShopRepository repository,
        IUnitOfWork unitOfWork,
        IMapper mapper,
        CancellationToken ct)
    {
        var shop = await repository.GetByIdAsync(command.ShopId, ct);
        if (shop is null)
            return (Response<ModerationShopDto>.Error(System.Net.HttpStatusCode.NotFound, "Coffee shop not found"), null);

        shop.AttachMenuPhotos(
            command.Photos.Select(p => (p.FileName, p.ContentType, p.StorageKey, p.Size)).ToArray(),
            DateTime.UtcNow);

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
