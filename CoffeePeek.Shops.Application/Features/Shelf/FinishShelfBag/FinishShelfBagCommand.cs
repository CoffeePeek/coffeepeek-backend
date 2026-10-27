namespace CoffeePeek.Shops.Application.Features.Shelf.FinishShelfBag;

public sealed record FinishShelfBagCommand(Guid UserId, Guid BagId, int? Rating, bool? BuyAgain);
