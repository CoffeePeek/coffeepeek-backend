namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public interface IQueryCoffeeShopRepository
{
    void Add(CoffeeShop shop);
    Task<bool> Exists(Guid id, CancellationToken ct = default);
    Task<(decimal Latitude, decimal Longitude)?> GetCoordinatesAsync(Guid id, CancellationToken ct = default);
    Task<Guid?> GetCityIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsByModerationId(Guid id, CancellationToken ct = default);
    Task<Guid?> GetIdByModerationId(Guid id, CancellationToken ct = default);
    Task<Dictionary<Guid, string>> GetShopNamesByIdsAsync(IEnumerable<Guid> shopIds, CancellationToken ct = default);
    Task<HashSet<Guid>> GetShopIdsByCityIdAsync(Guid cityId, CancellationToken ct = default);
    Task<HashSet<Guid>> GetShopIdsByCityIdsAsync(IReadOnlyCollection<Guid> cityIds, CancellationToken ct = default);
    /// <summary>Tracked shops that are either listed by id or located inside the given bounding box.</summary>
    Task<List<CoffeeShop>> ListForEnrichmentAsync(
        IReadOnlyCollection<Guid> shopIds,
        decimal minLat, decimal maxLat, decimal minLon, decimal maxLon,
        CancellationToken ct = default);
}
