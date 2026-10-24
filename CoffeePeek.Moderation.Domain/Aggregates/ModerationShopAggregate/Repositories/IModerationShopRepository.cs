using CoffeePeek.Shared.Domain.Interfaces.Persistance;

namespace CoffeePeek.Moderation.Domain.Aggregates;

public interface IModerationShopRepository
{
    Task<bool> Any(ISpecification<ModerationShop> spec, CancellationToken ct = default);
    Task<ModerationShop?> GetByIdWithOutDetails(Guid moderationShopId, CancellationToken ct = default);
    
    Task<ModerationShop?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(ModerationShop shop);
    Task<Guid?> GetAwaitingGeocodingCityIdAsync(Guid id, string submittedAddress, CancellationToken ct = default);
    Task<bool> TryApplyGeocodingAsync(
        Guid id, string submittedAddress, Guid cityId, decimal latitude, decimal longitude, CancellationToken ct = default);
}

