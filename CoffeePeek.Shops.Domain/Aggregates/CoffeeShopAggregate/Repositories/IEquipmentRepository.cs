namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public interface IEquipmentRepository
{
    Task<Equipment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Equipment?> GetByBrandAndModelAsync(string brand, string modelName, CancellationToken ct = default);
    Task<EquipmentCategory?> GetCategoryByIdAsync(int categoryId, CancellationToken ct = default);
    void Add(Equipment equipment);
    void Remove(Equipment equipment);
}
