namespace CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

public interface IQueryReviewRepository
{
    Task<Review?> GetById(Guid reviewId, CancellationToken ct);
    Task<HashSet<Guid>> GetHelpfulReviewIds(
        Guid userId,
        IReadOnlyCollection<Guid> reviewIds,
        CancellationToken ct);

    Task<(IReadOnlyList<Review> reviews, int totalCount)> GetByCoffeeShopId(
        Guid coffeeShopId,
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default);

    Task<(decimal averageRating, int count)> GetReviewStatsByCoffeeShopId(Guid coffeeShopId, CancellationToken ct);
}
