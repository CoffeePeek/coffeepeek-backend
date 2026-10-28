namespace CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;

public interface IQueryCheckInRepository
{
    void Add(CheckIn checkIn);
    Task<CheckIn?> GetTrackedByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ArePhotoKeysUsedAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default);
    Task<(bool IsHelpful, int HelpfulCount, bool Changed)> SetHelpfulAsync(Guid id, Guid userId, bool helpful, CancellationToken ct = default);

    Task<List<Guid>> GetVisitedShopIdsAsync(Guid userId, CancellationToken ct = default);
    Task<int> GetCheckInCountByCoffeeShopIdAsync(Guid coffeeShopId, CancellationToken ct = default);

    Task<Dictionary<Guid, int>> GetCheckInCountsByShopIdsAsync(IEnumerable<Guid> shopIds,
        CancellationToken ct = default);
    Task<bool> Exists(Guid userId, Guid shopDtoId, CancellationToken cancellationToken);
    Task<bool> ExistsSinceAsync(Guid userId, Guid shopId, DateTime sinceUtc, CancellationToken ct = default);
    Task<int> CountSinceAsync(Guid userId, DateTime sinceUtc, CancellationToken ct = default);
    Task<IReadOnlyList<CheckIn>> GetByUserAndShopAsync(
        Guid userId,
        Guid shopId,
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default);
    Task<bool> ExistsByIdAsync(Guid checkInId, CancellationToken ct = default);
    Task<Guid?> GetUserIdByIdAsync(Guid checkInId, CancellationToken ct = default);
    Task<(Guid Id, string Name, string RoasterName)?> GetPublishedCoffeeProductBySlugAsync(string slug, CancellationToken ct = default);
    /// <summary>
    /// The bean of a shelf bag owned by <paramref name="userId"/>, in any status; null for a missing bag or another owner's.
    /// </summary>
    Task<(Guid? CoffeeProductId, string? Name, string? RoasterName)?> GetOwnedShelfBagBeanAsync(Guid userId, Guid bagId,
        CancellationToken ct = default);

    /// <summary>
    /// Serializes check-in creation per user until the current transaction ends, so the cooldown and daily-cap
    /// checks cannot be raced by parallel requests.
    /// </summary>
    Task LockUserForCheckInAsync(Guid userId, CancellationToken ct = default);
}
