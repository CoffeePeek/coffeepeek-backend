using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using MapsterMapper;

namespace CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;

public static class GetCoffeeShopHandler
{
    public static async Task<Response<GetCoffeeShopResponse>> Handle(
        GetCoffeeShopQuery query,
        ICoffeeShopQueries shopQueries,
        IQueryCheckInRepository checkInRepository,
        IQueryReviewRepository reviewRepository,
        ICacheService redisService,
        IMapper mapper,
        CancellationToken ct)
    {
        var cacheKey = CacheKey.Shop.Detail(query.Id);
        var cacheTtl = ShopScheduleCachePolicy.UntilNextUtcMinute(DateTime.UtcNow);

        var shopDto = await redisService.GetAsync(cacheKey, async token =>
        {
            var dto = await shopQueries.GetDetailsById(query.Id, token);
            if (dto == null) return null;

            var (avgRating, count) = await reviewRepository.GetReviewStatsByCoffeeShopId(query.Id, token);
            var (reviews, _) = await reviewRepository.GetByCoffeeShopId(query.Id, 1, 10, token);

            return dto with { 
                Rating = avgRating, 
                ReviewCount = count, 
                Reviews = mapper.Map<ReviewDto[]>(reviews)
            };
        }, cacheTtl, ct);

        if (shopDto == null)
            return Response<GetCoffeeShopResponse>.Error("Shop not found");

        if (query.UserId.HasValue)
        {
            var userId = query.UserId.Value;
            var existingReviewId = await reviewRepository.GetExistingReviewIdByUserAndShopAsync(userId, query.Id, ct);
            shopDto = shopDto with
            {
                CanCreateReview = existingReviewId is null,
                ExistingReviewId = existingReviewId,
                IsVisited = await checkInRepository.Exists(userId, query.Id, ct),
                UserCheckIns = mapper.Map<CheckInDto[]>(
                    await checkInRepository.GetByUserAndShopAsync(userId, query.Id, 1, 10, ct)),
                Reviews = await PersonalizeHelpful(shopDto.Reviews, userId, reviewRepository, ct)
            };
        }

        return Response<GetCoffeeShopResponse>.Success(new GetCoffeeShopResponse(shopDto));
    }

    private static async Task<ReviewDto[]> PersonalizeHelpful(
        ReviewDto[] reviews,
        Guid userId,
        IQueryReviewRepository reviewRepository,
        CancellationToken ct)
    {
        if (reviews.Length == 0)
            return reviews;

        var reviewIds = reviews.Select(r => r.Id).ToArray();
        var helpfulIds = await reviewRepository.GetHelpfulReviewIds(userId, reviewIds, ct);

        return reviews
            .Select(r => r with { IsHelpfulByCurrentUser = helpfulIds.Contains(r.Id) })
            .ToArray();
    }
}
