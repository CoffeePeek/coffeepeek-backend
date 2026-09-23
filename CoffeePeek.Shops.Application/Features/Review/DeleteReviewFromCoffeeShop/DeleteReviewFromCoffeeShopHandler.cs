using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Public.Stats;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

namespace CoffeePeek.Shops.Application.Features.Review.DeleteReviewFromCoffeeShop;

public class DeleteReviewFromCoffeeShopHandler
{
    public async Task<Response> Handle(
        DeleteReviewFromCoffeeShopCommand request,
        IReviewRepository reviewRepository,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        CancellationToken cancellationToken)
    {
        var review = await reviewRepository.GetById(request.ReviewId, cancellationToken);

        if (review == null)
            throw new NotFoundException($"{nameof(Review)} not found by id");

        if (review.UserId != request.RequestingUserId)
            throw new ForbiddenException("You do not have permission to delete this review");

        review.SoftDelete();

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cacheService.RemoveAsync(CacheKey.Shop.Detail(review.CoffeeShopId));
        await PublicStatsCacheInvalidator.InvalidateAsync(cacheService, cancellationToken);
        await cacheService.RemoveByPattern(CacheKey.Shop.SearchPattern(), cancellationToken);
        return Response.Success();
    }
}
