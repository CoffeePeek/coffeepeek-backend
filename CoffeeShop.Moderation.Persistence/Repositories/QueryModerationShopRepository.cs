using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Aggregates.Enums;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeeShop.Moderation.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Moderation.Persistence.Repositories;

public class QueryModerationShopRepository(ModerationDbContext dbContext) : IQueryModerationShopRepository
{
    private readonly DbSet<ModerationShop> _repository = dbContext.ModerationShops;

    public Task<ModerationShop?> GetById(Guid id, CancellationToken ct = default)
    {
        return BuildReviewQuery().FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<(IReadOnlyList<ModerationShop> Items, int TotalCount)> GetPagedForReviewAsync(
        int page,
        int pageSize,
        ModerationStatus? status,
        string? search,
        CoffeeFocus? coffeeFocus,
        Guid? userId = null,
        CancellationToken ct = default)
    {
        var query = BuildReviewQuery();

        if (status.HasValue)
            query = query.Where(s => s.ModerationStatus == status.Value);

        if (userId.HasValue)
            query = query.Where(s => s.UserId == userId.Value);

        if (coffeeFocus.HasValue)
            query = query.Where(s => s.CoffeeFocus == coffeeFocus.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = ImportCandidateTextSearch.ToILikeContainsPattern(search);
            query = query.Where(s =>
                EF.Functions.ILike(s.Name, term, "\\") ||
                (s.Location != null && EF.Functions.ILike(s.Location.Address, term, "\\")));
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(s => s.CreatedAtUtc)
            .ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    private IQueryable<ModerationShop> BuildReviewQuery() =>
        _repository.AsNoTracking()
            .Include(s => s.Contact)
            .Include(s => s.Location)
            .Include(s => s.ShopPhotos)
            .Include(s => s.Schedules)
            .ThenInclude(sch => sch.Intervals)
            .Include(s => s.ModerationShopEquipments)
            .Include(s => s.ModerationCoffeeBeanShops)
            .Include(s => s.ModerationRoasterShops)
            .Include(s => s.ModerationShopBrewMethods)
            // Six collection includes in one query multiply rows (cartesian explosion); split per collection.
            .AsSplitQuery();
}
