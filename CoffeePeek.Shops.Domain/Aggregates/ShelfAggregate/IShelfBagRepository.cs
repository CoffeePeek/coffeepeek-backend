namespace CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;

public interface IShelfBagRepository
{
    /// <summary>Serializes shelf writes of one owner until the surrounding transaction ends.</summary>
    Task LockOwnerAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Tracked bag of any owner; callers check ownership.</summary>
    Task<ShelfBag?> GetAsync(Guid id, CancellationToken ct = default);
    Task<List<ShelfBag>> GetOnShelfAsync(Guid userId, CancellationToken ct = default);
    Task<List<ShelfBag>> GetAllAsync(Guid userId, CancellationToken ct = default);
    void Add(ShelfBag bag);
    void Remove(ShelfBag bag);
}
