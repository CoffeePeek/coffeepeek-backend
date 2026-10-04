using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeeShop.Moderation.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;
using ModerationReview = CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate.ModerationReview;

namespace CoffeeShop.Moderation.Persistence.Repositories;

public class ModerationReviewRepository(ModerationDbContext dbContext) : IModerationReviewRepository
{
    private readonly DbSet<ModerationReview> _reviewRepository = dbContext.ModerationReviews;
    
    public void AddPhotos(IEnumerable<CoffeePeek.Moderation.Domain.Entities.PhotoMetadata> photos)
    {
        // New GUID-keyed children of an already tracked review must be explicitly Added;
        // detecting them through the navigation alone can mark them Modified.
        dbContext.ShopPhotos.AddRange(photos);
    }

    public void Add(ModerationReview review)
    {
        _reviewRepository.Add(review);
    }
    
    public Task<ModerationReview?> GetById(Guid id, CancellationToken ct = default)
    {
        // Photos must be loaded: approve/edit handlers map this aggregate into the event that
        // Shops publishes from — without the Include every published review arrived photo-less.
        return _reviewRepository
            .Include(x => x.ReviewPhotos)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public Task<ModerationReview?> GetByShopId(Guid shopId, CancellationToken ct = default)
    {
        return _reviewRepository.FirstOrDefaultAsync(x => x.ShopId == shopId, ct);
    }
}