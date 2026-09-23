using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Moderation.Domain.Import;
using CoffeeShop.Moderation.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Moderation.Persistence.Repositories;

public class ShopImportCandidateRepository(ModerationDbContext dbContext) : IShopImportCandidateRepository
{
    public Task<ShopImportCandidate?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        dbContext.ShopImportCandidates.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<ShopImportCandidate?> GetById(Guid id, CancellationToken ct = default) =>
        dbContext.ShopImportCandidates.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyDictionary<Guid, ShopImportCandidate>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return new Dictionary<Guid, ShopImportCandidate>();

        var items = await dbContext.ShopImportCandidates
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(ct);

        return items.ToDictionary(c => c.Id);
    }

    public async Task<IReadOnlyDictionary<string, ShopImportCandidate>> GetByExternalIdsAsync(
        ImportSource source,
        IReadOnlyCollection<string> externalIds,
        CancellationToken ct = default)
    {
        if (externalIds.Count == 0)
            return new Dictionary<string, ShopImportCandidate>();

        var items = await dbContext.ShopImportCandidates
            .Where(c => c.Source == source && externalIds.Contains(c.ExternalId))
            .ToListAsync(ct);

        return items.ToDictionary(c => c.ExternalId);
    }

    public Task<List<ShopImportCandidate>> ListAllAsync(CancellationToken ct = default) =>
        dbContext.ShopImportCandidates.ToListAsync(ct);

    public void Add(ShopImportCandidate candidate) => dbContext.ShopImportCandidates.Add(candidate);

    public async Task<(IReadOnlyList<ShopImportCandidate> Items, int TotalCount)> SearchAsync(
        ImportQueueStatus? status,
        ImportCollectorBucket? bucket,
        ImportCoffeeFocus? focus,
        ImportRejectReason? rejectReason,
        string? search,
        bool excludeStale,
        int page,
        int pageSize,
        CancellationToken ct = default,
        ImportSource? source = null)
    {
        var query = dbContext.ShopImportCandidates.AsNoTracking();

        if (status.HasValue)
            query = query.Where(c => c.QueueStatus == status.Value);

        if (source.HasValue)
            query = query.Where(c => c.Source == source.Value);

        if (bucket.HasValue)
            query = query.Where(c => c.CollectorBucket == bucket.Value);
        else if (excludeStale)
            query = query.Where(c => c.CollectorBucket != ImportCollectorBucket.Stale);

        if (focus.HasValue)
            query = query.Where(c => c.CoffeeFocus == focus.Value);

        if (rejectReason.HasValue)
            query = query.Where(c => c.RejectReason == rejectReason.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = ImportCandidateTextSearch.ToILikeContainsPattern(search);
            query = query.Where(c =>
                (c.Name != null && EF.Functions.ILike(c.Name, term, "\\")) ||
                (c.Address != null && EF.Functions.ILike(c.Address, term, "\\")));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(c => c.CollectorBucket)
            .ThenBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<ImportCandidateStats> GetStatsAsync(CancellationToken ct = default)
    {
        var query = dbContext.ShopImportCandidates.AsNoTracking();

        var statusCounts = await query
            .GroupBy(c => c.QueueStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int CountOf(ImportQueueStatus status) =>
            statusCounts.FirstOrDefault(x => x.Status == status)?.Count ?? 0;

        var inFeed = await query.CountAsync(c => c.ResultingShopId != null, ct);

        var byFocusRows = await query
            .Where(c => c.CoffeeFocus != null)
            .GroupBy(c => c.CoffeeFocus)
            .Select(g => new { Focus = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var byFocus = byFocusRows
            .Where(x => x.Focus.HasValue)
            .ToDictionary(x => x.Focus!.Value, x => x.Count);

        var byBucket = await query
            .GroupBy(c => c.CollectorBucket)
            .Select(g => new { Bucket = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Bucket, x => x.Count, ct);

        var rejectedReasons = await query
            .Where(c => c.QueueStatus == ImportQueueStatus.Rejected)
            .GroupBy(c => c.RejectReason)
            .Select(g => new { Reason = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return new ImportCandidateStats(
            CountOf(ImportQueueStatus.Pending),
            CountOf(ImportQueueStatus.Skipped),
            CountOf(ImportQueueStatus.Published),
            CountOf(ImportQueueStatus.Rejected),
            inFeed,
            byFocus,
            byBucket,
            new ImportRejectedByReasonStats(
                rejectedReasons.Where(r => r.Reason == ImportRejectReason.Closed).Sum(r => r.Count),
                rejectedReasons.Where(r => r.Reason == ImportRejectReason.Invalid).Sum(r => r.Count),
                rejectedReasons.Where(r => r.Reason == ImportRejectReason.NotCoffee).Sum(r => r.Count),
                rejectedReasons.Where(r => r.Reason == ImportRejectReason.Duplicate).Sum(r => r.Count),
                rejectedReasons.Where(r => r.Reason is null).Sum(r => r.Count)));
    }

    public Task<List<ShopImportCandidate>> ListForDuplicateScanAsync(CancellationToken ct = default) =>
        dbContext.ShopImportCandidates
            .Where(c => c.QueueStatus != ImportQueueStatus.Rejected)
            .ToListAsync(ct);
}
