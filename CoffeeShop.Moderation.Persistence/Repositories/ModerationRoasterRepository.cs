using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeeShop.Moderation.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Moderation.Persistence.Repositories;

public class ModerationRoasterRepository(ModerationDbContext context) : IModerationRoasterRepository
{
    private DbSet<ModerationRoaster> Repository => context.ModerationRoasters;

    public Task<ModerationRoaster?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Repository
            .Include(r => r.Contact)
            .Include(r => r.Location)
            .Include(r => r.Photos)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task AddAsync(ModerationRoaster roaster)
    {
        ArgumentNullException.ThrowIfNull(roaster);
        await Repository.AddAsync(roaster);
    }

    public Task<bool> IsAwaitingGeocodingAsync(Guid id, string submittedAddress, CancellationToken ct = default) =>
        Repository.AsNoTracking().AnyAsync(roaster =>
            roaster.Id == id && roaster.ModerationStatus == ModerationStatus.Pending &&
            roaster.Location != null && roaster.Location.Address == submittedAddress &&
            !roaster.Location.IsAddressValidated, ct);

    public async Task<bool> TryApplyGeocodingAsync(
        Guid id, string submittedAddress, decimal latitude, decimal longitude, CancellationToken ct = default)
    {
        // This guard also makes a redelivered message harmless. The existing xmin concurrency
        // token protects a moderator who loaded the row before this update and saves afterward.
        var affected = await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "ModerationRoasters"
            SET "Location_Latitude" = {latitude},
                "Location_Longitude" = {longitude},
                "Location_IsAddressValidated" = TRUE,
                "UpdatedAtUtc" = {DateTime.UtcNow}
            WHERE "Id" = {id}
              AND "ModerationStatus" = {(int)ModerationStatus.Pending}
              AND "Location_Address" = {submittedAddress}
              AND "Location_IsAddressValidated" IS FALSE
            """, ct);
        return affected == 1;
    }
}
