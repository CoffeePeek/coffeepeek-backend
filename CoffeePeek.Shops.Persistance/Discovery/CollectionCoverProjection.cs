using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Persistance.Discovery;

internal static class CollectionCoverProjection
{
    internal static SavedCollectionCoverDto? Read(Guid collectionId, string visibility, Guid? photoId) => photoId is { } id
        ? new(id, $"/api/v1/{(visibility == "public" ? "favorite-collections" : "saved-collections")}/{collectionId}/cover/{id}")
        : null;
}
