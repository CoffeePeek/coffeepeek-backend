using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using MapsterMapper;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public static class GetOwnerRoastersHandler
{
    public static async Task<Response<OwnerRoastersPage>> Handle(GetOwnerRoastersQuery query,
        IQueryRoasterRepository repository, IMapper mapper, CancellationToken ct)
    {
        if (query.OwnerUserId == Guid.Empty)
            throw new ValidationException("Owner user ID is required.");
        if (query.Page < 1 || query.PageSize is < 1 or > 100 || (long)(query.Page - 1) * query.PageSize > int.MaxValue)
            throw new ValidationException("Page must be positive and pageSize must be between 1 and 100.");
        var (items, total) = await repository.GetByOwnerAsync(query.OwnerUserId, query.Page, query.PageSize, ct);
        return Response<OwnerRoastersPage>.Success(new(mapper.Map<RoasterDto[]>(items), total, query.Page, query.PageSize));
    }
}
