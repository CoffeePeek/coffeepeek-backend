using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Shops.Application.Features.Review;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Persistance.Queries;

public class ReviewQueries(ShopsDbContext dbContext, IMapper mapper) : IReviewQueries
{
    private readonly DbSet<Review> _repository = dbContext.Reviews;
    
    public async Task<(ReviewDto[] Items, int TotalCount)> GetReviewsByUserId(
        Guid userId, int pageNumber, int pageSize, CancellationToken ct = default)
    {
        // Hidden (pending re-moderation) and deleted reviews must not appear; order before paging so pages are stable.
        var query = _repository.AsNoTracking().Where(x => x.UserId == userId && !x.IsSoftDelete);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .ThenBy(r => r.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ProjectToType<ReviewDto>(mapper.Config)
            .ToArrayAsync(ct);

        return (items, total);
    }
}