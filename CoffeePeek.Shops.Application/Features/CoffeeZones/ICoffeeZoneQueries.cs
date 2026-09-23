namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public interface ICoffeeZoneQueries
{
    Task<AdminCoffeeZoneDto[]> GetAllAsync(Guid? cityId, CancellationToken ct = default);
    Task<AdminCoffeeZoneDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<CoffeeZoneMembershipPreviewDto?> PreviewMembershipAsync(Guid id, CancellationToken ct = default);
    Task<CoffeeZoneCandidateDto[]> GenerateCandidatesAsync(
        Guid cityId,
        int radiusMeters,
        int minShops,
        CancellationToken ct = default);
}
