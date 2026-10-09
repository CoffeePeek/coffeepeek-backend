using System.Linq.Expressions;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Persistance.Configuration;

namespace CoffeePeek.Shops.Persistance.Queries;

public static class CheckInEligibility
{
    public static readonly Expression<Func<CheckIn, bool>> CurrentPublic = c =>
        c.DeletedAtUtc == null && c.Visibility == CheckInVisibility.Public
        && c.ModerationState == CheckInModerationState.Approved && c.ApprovedRevision == c.ContentRevision;

    public static IQueryable<CheckIn> Public(ShopsDbContext db) => db.CheckIns.AsQueryable().Where(CurrentPublic)
        .Where(c => db.Shops.Any(s => s.Id == c.ShopId && !s.IsHidden));

    // NOT EXISTS selects the deterministic latest eligible score without client grouping or N+1 queries.
    public static IQueryable<CheckIn> LatestRatings(ShopsDbContext db)
    {
        var eligible = Public(db).Where(c => c.Rating != null);
        return eligible.Where(c => !eligible.Any(n => n.UserId == c.UserId && n.ShopId == c.ShopId
            && (n.VisitedAt > c.VisitedAt || (n.VisitedAt == c.VisitedAt && n.CreatedAtUtc > c.CreatedAtUtc)
                || (n.VisitedAt == c.VisitedAt && n.CreatedAtUtc == c.CreatedAtUtc && n.Id.CompareTo(c.Id) > 0))));
    }
}
