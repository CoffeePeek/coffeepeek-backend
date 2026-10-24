using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Common.Enums;
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

    public Task<Guid?> GetAwaitingGeocodingCityIdAsync(Guid id, string submittedAddress, CancellationToken ct = default) =>
        ShopRepository.AsNoTracking().Where(shop =>
            shop.Id == id && shop.ModerationStatus != ModerationStatus.Approved &&
            shop.Location != null && shop.Location.Address == submittedAddress &&
            !shop.Location.IsAddressValidated)
            .Select(shop => (Guid?)shop.CityId).SingleOrDefaultAsync(ct);

    public async Task<bool> TryApplyGeocodingAsync(
        Guid id, string submittedAddress, Guid cityId, decimal latitude, decimal longitude, CancellationToken ct = default)
    {
        // A single guarded UPDATE is required: a read-then-save could overwrite a moderator's
        // newer address or decision. PostgreSQL reevaluates this predicate after row locking.
        var affected = await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "ModerationShops"
            SET "Location_Latitude" = {latitude},
                "Location_Longitude" = {longitude},
                "Location_IsAddressValidated" = TRUE,
                "UpdatedAtUtc" = {DateTime.UtcNow}
            WHERE "Id" = {id}
              AND "ModerationStatus" <> {(int)ModerationStatus.Approved}
              AND "Location_Address" = {submittedAddress}
              AND "CityId" = {cityId}
              AND "Location_IsAddressValidated" IS FALSE
            """, ct);
        return affected == 1;
    }

}
