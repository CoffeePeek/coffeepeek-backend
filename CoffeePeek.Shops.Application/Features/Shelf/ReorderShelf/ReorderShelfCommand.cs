namespace CoffeePeek.Shops.Application.Features.Shelf.ReorderShelf;

public sealed record ReorderShelfCommand(Guid UserId, Guid[]? BagIds);
