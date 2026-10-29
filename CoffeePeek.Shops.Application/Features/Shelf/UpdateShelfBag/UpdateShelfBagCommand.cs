using CoffeePeek.Contract.Dtos.Shelf;

namespace CoffeePeek.Shops.Application.Features.Shelf.UpdateShelfBag;

public sealed record UpdateShelfBagCommand(Guid UserId, Guid BagId, UpdateShelfBagRequest Request);
