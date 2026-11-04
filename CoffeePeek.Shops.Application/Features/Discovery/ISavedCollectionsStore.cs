using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public interface ISavedCollectionsStore
{
    Task<SavedCollectionSummary[]> ListAsync(Guid owner, string? kind, string? slug, CancellationToken ct);
    Task<SavedCollectionDto?> GetAsync(Guid owner, Guid id, CancellationToken ct);
    Task<FavoriteCollectionDto?> GetPublicAsync(Guid id, CancellationToken ct);
    Task<SavedCollectionDto> CreateAsync(Guid owner, CreateSavedCollectionRequest request, CancellationToken ct);
    Task<SavedCollectionDto?> UpdateAsync(Guid owner, Guid id, UpdateSavedCollectionRequest request, CancellationToken ct);
    Task<SavedCollectionDto?> ReplaceItemsAsync(Guid owner, Guid id, ReplaceSavedCollectionItemsRequest request, CancellationToken ct);
    Task<SavedCollectionSummary[]> SetMembershipAsync(Guid owner, string kind, string slug, Guid[] collectionIds,
        CancellationToken ct, Guid[]? addedCollectionIds = null, Guid[]? removedCollectionIds = null);
    Task<bool> DeleteAsync(Guid owner, Guid id, CancellationToken ct);
    Task DeleteForOwnerAsync(Guid owner, CancellationToken ct);
    Task<FeaturedCollectionPreference> FeaturedAsync(Guid owner, CancellationToken ct);
    Task<FeaturedCollectionPreference> SetFeaturedAsync(Guid owner, Guid? id, CancellationToken ct);
    Task<SavedCollectionDto?> PublicFeaturedAsync(Guid owner, CancellationToken ct);
    Task<CollectionCoverAccess?> CoverAsync(Guid? owner, Guid id, Guid photoId, CancellationToken ct);
}
