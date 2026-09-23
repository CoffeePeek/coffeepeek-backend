using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Domain.Interfaces.Persistance;
using CoffeeShop.Moderation.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;
using ModerationShop = CoffeePeek.Moderation.Domain.Aggregates.ModerationShop;

namespace CoffeeShop.Moderation.Persistence.Repositories;

public class ModerationShopRepository(ModerationDbContext context) : IModerationShopRepository
{
    private DbSet<ModerationShop> ShopRepository => context.ModerationShops;
    public async Task<bool> Any(ISpecification<ModerationShop> spec, CancellationToken ct = default)
    {
        return await ShopRepository.AnyAsync(spec.Criteria, ct);
    }

    public async Task<ModerationShop?> GetByIdWithOutDetails(Guid moderationShopId, CancellationToken ct = default)
    {
        return await ShopRepository.FirstOrDefaultAsync(x => x.Id == moderationShopId , ct);
    }

    public async Task<ModerationShop?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await ShopRepository
            .Include(s => s.Contact)
            .Include(s => s.Location)
            .Include(s => s.ShopPhotos)
            .Include(s => s.Schedules)
                .ThenInclude(sch => sch.Intervals)
            .Include(s => s.ModerationShopEquipments)
            .Include(s => s.ModerationCoffeeBeanShops)
            .Include(s => s.ModerationRoasterShops)
            .Include(s => s.ModerationShopBrewMethods)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken: ct);
    }

    public async Task AddAsync(ModerationShop shop)
    {
        ArgumentNullException.ThrowIfNull(shop);
        await ShopRepository.AddAsync(shop);
    }

}


