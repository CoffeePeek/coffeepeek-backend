using CoffeePeek.Contract.Dtos.Shelf;

namespace CoffeePeek.Shops.Application.Features.Shelf.CreateShelfBag;

public sealed record CreateShelfBagCommand(Guid UserId, CreateShelfBagRequest Request);
