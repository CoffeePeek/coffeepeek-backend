namespace CoffeePeek.Shops.Application.Features.Shelf.RestoreShelfBag;

public sealed record RestoreShelfBagCommand(Guid UserId, Guid BagId, int? Position);
