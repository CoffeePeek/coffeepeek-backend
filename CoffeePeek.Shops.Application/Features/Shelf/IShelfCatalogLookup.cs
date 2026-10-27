namespace CoffeePeek.Shops.Application.Features.Shelf;

/// <summary>Shelf → CoffeeCatalog read port; never writes catalog rows.</summary>
public interface IShelfCatalogLookup
{
    Task<ShelfCatalogProduct?> FindPublishedBySlugAsync(string slug, CancellationToken ct);
    /// <summary>Current published slugs; unpublished or unknown products are absent.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetPublishedSlugsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct);
}
