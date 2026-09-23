namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;

public interface ICoffeeZoneRepository
{
    Task<CoffeeZone?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CoffeeZoneMembershipOverride?> GetOverrideAsync(Guid zoneId, Guid shopId, CancellationToken ct = default);
    Task<CoffeeZoneMembershipOverride?> GetPrimaryOverrideAsync(Guid shopId, CancellationToken ct = default);
    void Add(CoffeeZone zone);
    void AddOverride(CoffeeZoneMembershipOverride membershipOverride);
    void RemoveOverride(CoffeeZoneMembershipOverride membershipOverride);
}
