using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public static class SetFeaturedCollectionHandler
{
    public static Task<FeaturedCollectionPreference> Handle(SetFeaturedCollectionCommand command, ISavedCollectionsStore collections, CancellationToken ct) =>
        collections.SetFeaturedAsync(command.OwnerId, command.Id, ct);
}
