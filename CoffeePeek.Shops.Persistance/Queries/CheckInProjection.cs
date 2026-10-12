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
        return rows.Select(c => CoffeePeek.Shops.Application.Features.CheckIn.CheckInMapping.ToDto(c,
            shops.GetValueOrDefault(c.ShopId)?.Name ?? "", helpful: helpfulIds.Contains(c.Id)) with
        {
            RejectionReason = c.UserId == readerId ? c.RejectionReason : null
        }).ToArray();
    }
}
