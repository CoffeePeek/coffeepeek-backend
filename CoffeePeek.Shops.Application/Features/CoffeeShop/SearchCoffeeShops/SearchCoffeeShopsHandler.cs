using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Common.Responses;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeShop.SearchCoffeeShops;

public class SearchCoffeeShopsHandler
{
    public static async Task<Response<GetCoffeeShopsResponse>> Handle(
        SearchCoffeeShopsQuery queryRequest,
        ICoffeeShopQueries coffeeShopQueries,
        IQueryCheckInRepository visitRepository,
        CancellationToken ct)
    {
        var (items, totalCount) = await coffeeShopQueries.Search(queryRequest, ct);
        var response = new GetCoffeeShopsResponse
        {
            CoffeeShops = items,
            TotalItems = totalCount,
            CurrentPage = queryRequest.PageNumber,
            PageSize = queryRequest.PageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)queryRequest.PageSize)
        };

        if (queryRequest.UserId.HasValue)
        {
            var visitedIds = await visitRepository.GetVisitedShopIdsAsync(queryRequest.UserId.Value, ct);
            foreach (var shop in response.CoffeeShops)
                shop.IsVisited = visitedIds.Contains(shop.Id);
        }

        return Response<GetCoffeeShopsResponse>.Success(response);
    }
}
