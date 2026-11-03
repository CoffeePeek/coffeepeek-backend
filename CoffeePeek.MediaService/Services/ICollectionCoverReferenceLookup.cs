namespace CoffeePeek.MediaService.Services;

public interface ICollectionCoverReferenceLookup
{
    /// <summary>Null means that the authority could not be reached; cleanup must retain the object.</summary>
    Task<bool?> IsCurrentAsync(Guid owner, Guid collectionId, Guid photoId, CancellationToken ct);
}
