using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public static class SetSavedCollectionMembershipHandler
{
    public static Task<SavedCollectionSummary[]> Handle(SetSavedCollectionMembershipCommand command, ISavedCollectionsStore collections, CancellationToken ct) =>
        collections.SetMembershipAsync(command.OwnerId, command.Kind, command.Slug, command.CollectionIds, ct,
            command.AddedCollectionIds, command.RemovedCollectionIds);
}
