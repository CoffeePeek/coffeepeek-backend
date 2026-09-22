using CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeeShop.Moderation.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Moderation.Persistence.Repositories;

public class ShopChangeRequestRepository(ModerationDbContext dbContext) : IShopChangeRequestRepository
{
    private readonly DbSet<ShopChangeRequest> _requests = dbContext.ShopChangeRequests;

    public void Add(ShopChangeRequest request) => _requests.Add(request);

    public Task<ShopChangeRequest?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _requests.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<ShopChangeRequest?> GetByIdForUserAsync(Guid id, Guid userId, CancellationToken ct = default) =>
        _requests.AsNoTracking().FirstOrDefaultAsync(
            x => x.Id == id && x.SubmittedByUserId == userId, ct);

    public async Task<(IReadOnlyList<ShopChangeRequest> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        ModerationStatus? status,
        Guid? shopId,
        Guid? submittedByUserId,
        ShopChangeSection? section,
        CancellationToken ct = default)
    {
        var query = _requests.AsNoTracking().AsQueryable();
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (shopId.HasValue) query = query.Where(x => x.ShopId == shopId.Value);
        if (submittedByUserId.HasValue) query = query.Where(x => x.SubmittedByUserId == submittedByUserId.Value);
        if (section.HasValue) query = query.Where(x => x.Section == section.Value);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, totalCount);
    }
}
