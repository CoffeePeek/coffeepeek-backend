using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeeShop.Moderation.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;
using ModerationReview = CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate.ModerationReview;

namespace CoffeeShop.Moderation.Persistence.Repositories;

public class ModerationReviewRepository(ModerationDbContext dbContext) : IModerationReviewRepository
{
    private readonly DbSet<ModerationReview> _reviewRepository = dbContext.ModerationReviews;

    public void Add(ModerationReview review)
    {
        _reviewRepository.Add(review);
    }

    public Task<ModerationReview?> GetById(Guid id, CancellationToken ct = default)
    {
        return _reviewRepository
            .Include(x => x.ReviewPhotos)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public Task<ModerationReview?> GetByCheckInRevision(Guid checkInId, int contentRevision, CancellationToken ct = default)
    {
        return _reviewRepository.Include(x => x.ReviewPhotos)
            .FirstOrDefaultAsync(x => x.CheckInId == checkInId && x.ContentRevision == contentRevision, ct);
    }

}
