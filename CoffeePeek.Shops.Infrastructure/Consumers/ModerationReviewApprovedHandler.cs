using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Contract.Events.Shops;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Features.Public.Stats;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

namespace CoffeePeek.Shops.Infrastructure.Consumers;

public static class ModerationReviewApprovedHandler
{
    public static async Task<ReviewAddedEvent?> Handle(
        ModerationReviewApprovedEvent @event,
        IReviewRepository reviewRepository,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        CancellationToken ct)
    {
        var reviewDto = @event.Review;
        
        var exists = await reviewRepository.AnyAsync(reviewDto.ShopId, reviewDto.UserId, ct);

        if (exists)
        {
            return null;
        }
        
        var review = Review.Create(
            reviewDto.ShopId,
            reviewDto.UserId,
            reviewDto.UserName,
            reviewDto.Header,
            reviewDto.Comment,
            reviewDto.Rating.Place, reviewDto.Rating.Service, reviewDto.Rating.Coffee,
            reviewDto.Id);

        reviewRepository.Add(review);

        await unitOfWork.SaveChangesAsync(ct);
        await PublicStatsCacheInvalidator.InvalidateAsync(cacheService, ct);
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);
        
        return new ReviewAddedEvent(reviewDto.UserId, reviewDto.ShopId, review.Id);
    }
}
