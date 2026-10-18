using CoffeePeek.Shops.Application.Features.Feed;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Queries;

public sealed class CheckInFeedReader(ShopsDbContext db, CoffeePeek.Shops.Application.Abstractions.IAuthorVisibilityLookup visibility) : ICheckInFeedReader
{
    public async Task<CheckInFeedReadPage> ReadAsync(CheckInFeedScope scope, int pageSize,
        CheckInFeedPosition? after, Guid? readerId, CancellationToken ct)
    {
        var excluded = await visibility.ExcludedUserIdsAsync(readerId, ct);
        var query = BuildQuery(db, scope, after).Where(c => !excluded.Contains(c.UserId));
        var rows = await query.AsNoTracking().Include(c => c.ShopPhotos).Take(pageSize + 1).ToArrayAsync(ct);
        var hasMore = rows.Length > pageSize;
        rows = rows.Take(pageSize).ToArray();
        var items = await CheckInProjection.FromRowsAsync(db, rows, readerId, ct);
        return new(items.Select((item, i) => new CheckInFeedEntry(item, rows[i].FirstPublishedAtUtc!.Value)).ToArray(), hasMore);
    }

    public static IOrderedQueryable<CheckIn> BuildQuery(ShopsDbContext db, CheckInFeedScope scope,
        CheckInFeedPosition? after)
    {
        var query = CheckInEligibility.Public(db).Where(c => c.FirstPublishedAtUtc != null);
        if (scope.CityId is { } cityId)
            query = query.Where(c => db.Shops.Any(s => s.Id == c.ShopId && s.Location.CityId == cityId));
        if (scope.ShopId is { } shopId) query = query.Where(c => c.ShopId == shopId);
        if (scope.AuthorId is { } authorId) query = query.Where(c => c.UserId == authorId);
        if (after is not null)
            query = query.Where(c => c.FirstPublishedAtUtc < after.PublishedAtUtc
                || (c.FirstPublishedAtUtc == after.PublishedAtUtc && c.Id.CompareTo(after.CheckInId) < 0));
        return query.OrderByDescending(c => c.FirstPublishedAtUtc).ThenByDescending(c => c.Id);
    }
}
