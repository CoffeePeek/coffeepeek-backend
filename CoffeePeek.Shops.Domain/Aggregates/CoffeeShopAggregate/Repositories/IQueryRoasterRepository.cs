namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public interface IQueryRoasterRepository
{
    Task<Roaster[]> GetAll();
    Task<IEnumerable<Roaster>> GetByIds(List<Guid> ids, CancellationToken cancellationToken);
    Task<Roaster?> GetByIdWithShopsAndPhotosAsync(Guid id, CancellationToken ct = default);
    Task<Guid?> GetIdByModerationId(Guid id, CancellationToken ct = default);
}