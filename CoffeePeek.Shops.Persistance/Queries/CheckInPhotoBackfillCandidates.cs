using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shared.Kernel.Media;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;
using DomainKind = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckInKind;

namespace CoffeePeek.Shops.Persistance.Queries;

/// <summary>Read only observations of canonical Community bindings, never gallery or legacy bridge rows.</summary>
public sealed class CheckInPhotoBackfillCandidates(ShopsDbContext db, TimeProvider clock) : ICheckInPhotoBackfillCandidates
{
    public async Task<PhotoBackfillPage> ReadAsync(PhotoBackfillPageRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.IsValid(clock.GetUtcNow().UtcDateTime)) throw new ArgumentException("Invalid photo inventory page.", nameof(request));
        var publicIds = CheckInEligibility.Public(db).Select(c => c.Id);
        var checkIns = db.CheckIns.AsNoTracking().Where(c => c.DeletedAtUtc == null && c.CreatedAtUtc <= request.BeforeUtc);
        checkIns = request.Phase switch
        {
            PhotoBackfillPhase.RecentPublicShop => checkIns.Where(c => c.Kind == DomainKind.Shop && publicIds.Contains(c.Id)
                && (c.FirstPublishedAtUtc ?? c.CreatedAtUtc) >= request.RecentSinceUtc),
            PhotoBackfillPhase.PublicShopArchive => checkIns.Where(c => c.Kind == DomainKind.Shop && publicIds.Contains(c.Id)
                && (c.FirstPublishedAtUtc ?? c.CreatedAtUtc) < request.RecentSinceUtc),
            PhotoBackfillPhase.OtherShop => checkIns.Where(c => c.Kind == DomainKind.Shop && !publicIds.Contains(c.Id)),
            PhotoBackfillPhase.Home => checkIns.Where(c => c.Kind == DomainKind.Home),
            _ => throw new ArgumentException("Unknown photo inventory phase.", nameof(request))
        };
        var sortByPublication = request.Phase is PhotoBackfillPhase.RecentPublicShop or PhotoBackfillPhase.PublicShopArchive;
        var query = checkIns.Join(db.CheckInPhotos.AsNoTracking().Where(p => p.CreatedAtUtc <= request.BeforeUtc),
            c => c.Id, p => p.CanonicalCheckInId, (c, p) => new
            {
                CheckInId = c.Id, c.UserId, c.Kind, c.DrinkPhotoId,
                PhotoId = p.Id, p.OwnerId, p.StorageKey, p.ContentType, p.SizeBytes,
                SortAtUtc = sortByPublication ? c.FirstPublishedAtUtc ?? c.CreatedAtUtc : c.CreatedAtUtc
            }).Where(x => x.UserId == x.OwnerId && x.SortAtUtc <= request.BeforeUtc);
        if (request.Cursor is { } cursor)
            query = query.Where(x => x.SortAtUtc < cursor.SortAtUtc
                || x.SortAtUtc == cursor.SortAtUtc && x.CheckInId.CompareTo(cursor.CheckInId) < 0
                || x.SortAtUtc == cursor.SortAtUtc && x.CheckInId == cursor.CheckInId && x.PhotoId.CompareTo(cursor.PhotoId) < 0);
        var rows = await query.OrderByDescending(x => x.SortAtUtc).ThenByDescending(x => x.CheckInId)
            .ThenByDescending(x => x.PhotoId).Take(request.Limit + 1).ToArrayAsync(ct);
        var items = rows.Take(request.Limit).Select(x => new PhotoBackfillCandidate(x.PhotoId, x.CheckInId,
            x.OwnerId, x.StorageKey, CheckInPhotoVariantIdentity.SourceVersion(x.PhotoId, x.StorageKey),
            (CheckInKind)x.Kind, x.PhotoId == x.DrinkPhotoId, x.SortAtUtc, x.SizeBytes, x.ContentType)).ToArray();
        var next = rows.Length > request.Limit
            ? new PhotoBackfillCursor(items[^1].SortAtUtc, items[^1].CheckInId, items[^1].PhotoId) : null;
        // A cursor is for one bounded pass, not a delta stream: visibility and late commits may
        // move rows between phases. The next inventory pass must restart without a cursor.
        return new PhotoBackfillPage(items, next);
    }
}
