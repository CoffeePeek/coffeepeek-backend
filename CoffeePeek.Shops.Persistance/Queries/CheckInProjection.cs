using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Queries;

internal static class CheckInProjection
{
    public static async Task<UnifiedCheckInDto[]> FromRowsAsync(ShopsDbContext db,
        CheckIn[] rows, Guid? readerId, CancellationToken ct, bool advertiseVariants = false)
    {
        if (rows.Length == 0) return [];
        var shopIds = rows.Where(c => c.Kind == CheckInKind.Shop && c.ShopId.HasValue)
            .Select(c => c.ShopId!.Value).Distinct().ToArray();
        var shops = await db.Shops.AsNoTracking().Where(s => shopIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, ct);
        var ids = rows.Select(c => c.Id).ToArray();
        var helpful = readerId == null ? [] : await db.CheckInHelpfulVotes.AsNoTracking()
            .Where(v => v.UserId == readerId && ids.Contains(v.CheckInId)).Select(v => v.CheckInId).ToArrayAsync(ct);
        var helpfulIds = helpful.ToHashSet();
        var items = rows.Select(c => CoffeePeek.Shops.Application.Features.CheckIn.CheckInMapping.ToDto(c,
            shops.GetValueOrDefault(c.ShopId ?? Guid.Empty)?.Name ?? "", helpful: helpfulIds.Contains(c.Id)) with
        {
            RejectionReason = c.UserId == readerId ? c.RejectionReason : null
        }).ToArray();
        var variants = await CheckInPhotoVariantBatch.ReadAsync(db, items.SelectMany(c =>
            CheckInPhotoVariantBatch.Photos(c.Photos, c.DrinkPhoto, c.ShelfBagPhoto).Select(p => (c.Id, p))), advertiseVariants, ct);
        return items.Select(c => c with
        {
            Photos = c.Photos.Select(p => CheckInPhotoVariantBatch.WithVariants(p, variants)).ToArray(),
            DrinkPhoto = c.DrinkPhoto is null ? null : CheckInPhotoVariantBatch.WithVariants(c.DrinkPhoto, variants),
            ShelfBagPhoto = c.ShelfBagPhoto is null ? null : CheckInPhotoVariantBatch.WithVariants(c.ShelfBagPhoto, variants)
        }).ToArray();
    }
}
