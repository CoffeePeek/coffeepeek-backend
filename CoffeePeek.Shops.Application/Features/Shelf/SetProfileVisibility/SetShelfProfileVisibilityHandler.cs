using CoffeePeek.Contract.Dtos.Shelf;

namespace CoffeePeek.Shops.Application.Features.Shelf.SetProfileVisibility;

public static class SetShelfProfileVisibilityHandler
{
    public static Task<ShelfProfileVisibilityDto> Handle(SetShelfProfileVisibilityCommand command,
        IShelfProfileStore profiles, CancellationToken ct) =>
        profiles.SetVisibilityAsync(command.OwnerId, command.IsVisible, ct);
}
