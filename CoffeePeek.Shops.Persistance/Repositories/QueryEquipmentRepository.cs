using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

public class QueryEquipmentRepository(ShopsDbContext dbContext) : IQueryEquipmentRepository
{
    private readonly DbSet<Equipment> _repository = dbContext.Equipments;
    
    public Task<Equipment[]> GetAll(CancellationToken ct = default)
    {
        return _repository.AsNoTracking().ToArrayAsync(ct);
    }

    public async Task<IEnumerable<Equipment>> GetByIds(List<Guid> ids, CancellationToken ct)
    {
        return await _repository
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(ct);
    }
}

public class EquipmentRepository(ShopsDbContext dbContext) : IEquipmentRepository
{
    public Task<Equipment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        dbContext.Equipments.FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<Equipment?> GetByBrandAndModelAsync(string brand, string modelName, CancellationToken ct = default) =>
        dbContext.Equipments.FirstOrDefaultAsync(e => e.Brand == brand && e.ModelName == modelName, ct);

    public Task<EquipmentCategory?> GetCategoryByIdAsync(int categoryId, CancellationToken ct = default) =>
        dbContext.EquipmentCategories.FirstOrDefaultAsync(c => c.Id == categoryId, ct);

    public void Add(Equipment equipment) => dbContext.Equipments.Add(equipment);

    public void Remove(Equipment equipment) => dbContext.Equipments.Remove(equipment);
}