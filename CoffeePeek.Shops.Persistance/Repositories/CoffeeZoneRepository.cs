using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Repositories;

public sealed class CoffeeZoneRepository(ShopsDbContext dbContext) : ICoffeeZoneRepository
{
    public Task<CoffeeZone?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        dbContext.CoffeeZones.FirstOrDefaultAsync(z => z.Id == id, ct);

    public Task<CoffeeZoneMembershipOverride?> GetOverrideAsync(Guid zoneId, Guid shopId, CancellationToken ct = default) =>
        dbContext.CoffeeZoneMembershipOverrides.FirstOrDefaultAsync(x => x.ZoneId == zoneId && x.ShopId == shopId, ct);

    public Task<CoffeeZoneMembershipOverride?> GetPrimaryOverrideAsync(Guid shopId, CancellationToken ct = default) =>
        dbContext.CoffeeZoneMembershipOverrides.FirstOrDefaultAsync(
            x => x.ShopId == shopId && x.Kind == CoffeeZoneMembershipOverrideKind.Primary, ct);

    public void Add(CoffeeZone zone) => dbContext.CoffeeZones.Add(zone);
    public void AddOverride(CoffeeZoneMembershipOverride membershipOverride) =>
        dbContext.CoffeeZoneMembershipOverrides.Add(membershipOverride);
    public void RemoveOverride(CoffeeZoneMembershipOverride membershipOverride) =>
        dbContext.CoffeeZoneMembershipOverrides.Remove(membershipOverride);
}
