using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Moderation.Domain.Import;
using CoffeeShop.Moderation.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Moderation.Persistence.Repositories;

public class QueryModerationReviewRepository(ModerationDbContext dbContext) : IQueryModerationReviewRepository
{
    private readonly DbSet<ModerationReview> _reviewRepository = dbContext.ModerationReviews;

    public Task<ModerationReview[]> GetAll(CancellationToken ct = default)
    {
        return _reviewRepository.AsNoTracking().ToArrayAsync(ct);
    }

    public Task<ModerationReview?> GetById(Guid id, CancellationToken ct = default)
    {
        return _reviewRepository.AsNoTracking()
            .Include(r => r.ReviewPhotos)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<(ModerationReview[] Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        ModerationStatus? status,
        string? search,
        CancellationToken ct = default)
    {
        var query = _reviewRepository.AsNoTracking().Include(r => r.ReviewPhotos).AsQueryable();

        if (status.HasValue)
            query = query.Where(r => r.ModerationStatus == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = ImportCandidateTextSearch.ToILikeContainsPattern(search);
            query = query.Where(r =>
                EF.Functions.ILike(r.Header ?? string.Empty, term, "\\") ||
                EF.Functions.ILike(r.Comment, term, "\\") ||
                EF.Functions.ILike(r.UserName, term, "\\"));
        }

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(ct);

        return (items, totalCount);
    }
}
