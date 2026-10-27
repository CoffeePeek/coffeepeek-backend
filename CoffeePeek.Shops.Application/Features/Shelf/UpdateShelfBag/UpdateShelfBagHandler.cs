using CoffeePeek.Contract.Dtos.Shelf;
using CoffeePeek.Contract.Events;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;
using Wolverine;

namespace CoffeePeek.Shops.Application.Features.Shelf.UpdateShelfBag;

public static class UpdateShelfBagHandler
{
    public static async Task<Response<ShelfBagDto>> Handle(UpdateShelfBagCommand command, IShelfBagRepository bags,
        IShelfCatalogLookup catalog, IShelfBagPhotoStore photos, IMessageBus bus, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var request = command.Request ?? throw new ValidationException("Request body is required.");
        var storageKey = ShelfBagInput.StorageKey(request.Photo);
        await bags.LockOwnerAsync(command.UserId, ct);
        var bag = await ShelfBagAccess.GetOwnedAsync(bags, command.UserId, command.BagId, ct);
        var details = await ShelfBagInput.ResolveUpdateAsync(request.CoffeeProductId, request.CoffeeProductSlug,
            request.CustomName, request.RoasterName, request.WeightGrams, request.Grind, request.RoastedOn, bag, catalog, ct);
        if (storageKey == null && bag.PhotoId == null && details.Name == null)
            throw new ValidationException("A shelf bag needs a photo or a bean (CoffeeProductSlug or CustomName).");
        ShelfBag.EnsureValid(details, storageKey != null || bag.PhotoId != null, DateTime.UtcNow);

        var photo = storageKey == null ? null : await ShelfBagInput.ClaimAsync(photos, command.UserId, bag.Id, storageKey, ct);
        var now = DateTime.UtcNow;
        if (bag.UpdateDetails(details, photo, now) is { } released)
            await bus.PublishAsync(new ShelfBagPhotoReleasedEvent(command.UserId, bag.Id, released, now));
        await unitOfWork.SaveChangesAsync(ct);
        return Response<ShelfBagDto>.Success(await ShelfBagMapping.ToDtoAsync(bag, catalog, ct));
    }
}
