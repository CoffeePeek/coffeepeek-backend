namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public interface IQueryRoasterRepository
{
    Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default);
    Task<Roaster[]> GetAll();
    Task<IEnumerable<Roaster>> GetByIds(List<Guid> ids, CancellationToken cancellationToken);
    Task<Roaster?> GetByIdWithShopsAndPhotosAsync(Guid id, CancellationToken ct = default);
    Task<Guid?> GetIdByModerationId(Guid id, CancellationToken ct = default);
    Task<(Roaster[] Items, int TotalCount)> GetByOwnerAsync(Guid ownerUserId, int page, int pageSize, CancellationToken ct);
}
