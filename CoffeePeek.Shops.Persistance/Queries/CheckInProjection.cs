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
        CheckIn[] rows, Guid? readerId, CancellationToken ct)
    {
        if (rows.Length == 0) return [];
        var shopIds = rows.Select(c => c.ShopId).Distinct().ToArray();
        var shops = await db.Shops.AsNoTracking().Where(s => shopIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, ct);
        var ids = rows.Select(c => c.Id).ToArray();
        var helpful = readerId == null ? [] : await db.CheckInHelpfulVotes.AsNoTracking()
            .Where(v => v.UserId == readerId && ids.Contains(v.CheckInId)).Select(v => v.CheckInId).ToArrayAsync(ct);
        var helpfulIds = helpful.ToHashSet();
        return rows.Select(c => new UnifiedCheckInDto
        {
            Id = c.Id, UserId = c.UserId, Username = c.Username, ShopId = c.ShopId,
            ShopName = shops.GetValueOrDefault(c.ShopId)?.Name ?? "", Text = c.Note,
            Rating = new RatingDto { Place = c.Rating.Place, Service = c.Rating.Service, Coffee = c.Rating.Coffee },
            VisitedAt = c.VisitedAt, CreatedAtUtc = c.CreatedAtUtc,
            Visibility = (Contract.Enums.CheckInVisibility)c.Visibility,
            ModerationState = (Contract.Enums.CheckInModerationState)c.ModerationState,
            ContentRevision = c.ContentRevision, RejectionReason = c.UserId == readerId ? c.RejectionReason : null,
            Drink = c.DrinkSlug == null ? null : new CheckInDrinkDto(c.DrinkSlug, c.CustomDrinkName, c.DrinkNameRu!, c.DrinkNameEn!),
            Photos = c.ShopPhotos.OrderBy(p => p.SortIndex).ThenBy(p => p.Id)
                .Select(p => new CheckInPhotoDto(p.Id, p.FileName, p.ContentType, p.StorageKey, p.SizeBytes, p.SortIndex,
                    $"/api/v1/check-ins/{c.Id}/photos/{p.Id}")).ToArray(),
            HelpfulCount = c.HelpfulCount, IsHelpfulByCurrentUser = helpfulIds.Contains(c.Id)
        }).ToArray();
    }
}
