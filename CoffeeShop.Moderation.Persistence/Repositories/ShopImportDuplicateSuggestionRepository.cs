using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeeShop.Moderation.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Moderation.Persistence.Repositories;

public class ShopImportDuplicateSuggestionRepository(ModerationDbContext dbContext)
    : IShopImportDuplicateSuggestionRepository
{
    public Task<ShopImportDuplicateSuggestion?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        dbContext.ShopImportDuplicateSuggestions.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<HashSet<(Guid Left, Guid Right)>> ListPairKeysAsync(CancellationToken ct = default)
    {
        var rows = await dbContext.ShopImportDuplicateSuggestions
            .AsNoTracking()
            .Select(s => new { s.LeftCandidateId, s.RightCandidateId })
            .ToListAsync(ct);

        return rows.Select(r => (r.LeftCandidateId, r.RightCandidateId)).ToHashSet();
    }

    public async Task<(IReadOnlyList<ShopImportDuplicateSuggestion> Items, int TotalCount)> SearchAsync(
        ImportDuplicateStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = dbContext.ShopImportDuplicateSuggestions.AsNoTracking();
        if (status.HasValue)
            query = query.Where(s => s.Status == status.Value);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.DistanceMeters)
            .ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<int> CountPendingAsync(CancellationToken ct = default) =>
        dbContext.ShopImportDuplicateSuggestions.CountAsync(s => s.Status == ImportDuplicateStatus.Pending, ct);

    public void AddRange(IReadOnlyList<ShopImportDuplicateSuggestion> suggestions) =>
        dbContext.ShopImportDuplicateSuggestions.AddRange(suggestions);
}
