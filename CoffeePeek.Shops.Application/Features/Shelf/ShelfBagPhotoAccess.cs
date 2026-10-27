namespace CoffeePeek.Shops.Application.Features.Shelf;

public sealed record ShelfBagPhotoAccess(Guid OwnerId, Guid BagId, Guid PhotoId, string ContentType);
