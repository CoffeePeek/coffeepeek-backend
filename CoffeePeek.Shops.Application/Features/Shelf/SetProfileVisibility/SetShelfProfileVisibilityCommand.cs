namespace CoffeePeek.Shops.Application.Features.Shelf.SetProfileVisibility;

public sealed record SetShelfProfileVisibilityCommand(Guid OwnerId, bool IsVisible);
