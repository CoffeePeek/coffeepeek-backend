using CoffeePeek.Contract.Dtos.Shelf;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;

namespace CoffeePeek.Shops.Application.Features.Shelf.CreateShelfBag;

public static class CreateShelfBagHandler
{
    public static async Task<Response<ShelfBagDto>> Handle(CreateShelfBagCommand command, IShelfBagRepository bags,
        IShelfCatalogLookup catalog, IShelfBagPhotoStore photos, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var request = command.Request ?? throw new ValidationException("Request body is required.");
        var storageKey = ShelfBagInput.StorageKey(request.Photo);
        var details = await ShelfBagInput.ResolveCreateAsync(request.CoffeeProductSlug, request.CustomName,
            request.RoasterName, request.WeightGrams, request.Grind, request.RoastedOn, catalog, ct);
        if (storageKey == null && details.Name == null)
            throw new ValidationException("A shelf bag needs a photo or a bean (CoffeeProductSlug or CustomName).");
        ShelfBag.EnsureValid(details, storageKey != null, DateTime.UtcNow);

        var id = ShelfBagInput.CreateId(command.UserId, storageKey);
        await bags.LockOwnerAsync(command.UserId, ct);
        var lineup = new ShelfLineup(command.UserId, await bags.GetOnShelfAsync(command.UserId, ct));
        lineup.EnsureRoom();
        ShelfBagPhoto? photo = null;
        if (storageKey != null)
        {
            if (await bags.GetAsync(id, ct) != null)
                throw new ConflictException("The upload is already attached to another bag.");
            photo = await ShelfBagInput.ClaimAsync(photos, command.UserId, id, storageKey, ct);
        }

        var bag = ShelfBag.Create(id, command.UserId, details, photo, DateTime.UtcNow);
        lineup.AddToFront(bag);
        bags.Add(bag);
        await unitOfWork.SaveChangesAsync(ct);
        return Response<ShelfBagDto>.Success(await ShelfBagMapping.ToDtoAsync(bag, catalog, ct));
    }
}
