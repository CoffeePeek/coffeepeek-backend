using System.Security.Cryptography;
using System.Text;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Common.Responses;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeShop.SearchCoffeeShops;

public class SearchCoffeeShopsHandler
{
    public static async Task<Response<GetCoffeeShopsResponse>> Handle(SearchCoffeeShopsQuery queryRequest,
        ICoffeeShopQueries coffeeShopQueries,
        IQueryCheckInRepository visitRepository,
        ICacheService redisService,
        CancellationToken ct)
    {
        var searchHash = CreateSearchHash(queryRequest);
        var cacheKey = CacheKey.Shop.Search(searchHash);
        var cacheTtl = ShopScheduleCachePolicy.UntilNextUtcMinute(DateTime.UtcNow);

        var cachedResponse = await redisService.GetAsync(cacheKey, async token =>
        {
            var (items, totalCount) = await coffeeShopQueries.Search(queryRequest, token);
            
            return new GetCoffeeShopsResponse
            {
                CoffeeShops = items,
                TotalItems = totalCount,
                CurrentPage = queryRequest.PageNumber,
                PageSize = queryRequest.PageSize,
                TotalPages = (int)Math.Ceiling(totalCount / (double)queryRequest.PageSize)
            };
        }, expiration: cacheTtl, cancellationToken: ct);
        
        if (cachedResponse == null) return Response<GetCoffeeShopsResponse>.Error("Failed to retrieve coffee shop search results");

        // Old cache entries omitted PageSize (defaulted to 0). Echo the requested size so
        // clients never paginate with pageSize=0.
        cachedResponse.PageSize = queryRequest.PageSize;

        if (queryRequest.UserId.HasValue)
        {
            var userId = queryRequest.UserId.Value;
            var visitedIds = await visitRepository.GetVisitedShopIdsAsync(userId, ct);

            foreach (var shop in cachedResponse.CoffeeShops)
            {
                shop.IsVisited = visitedIds.Contains(shop.Id);
            }
        }

        return Response<GetCoffeeShopsResponse>.Success(cachedResponse);
    }

    public static string CreateSearchHash(SearchCoffeeShopsQuery query)
    {
        var keyBuilder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            var queryHash = ComputeHash(query.Query.Trim().ToLowerInvariant());
            keyBuilder.Append($"q:{queryHash}:");
        }

        if (query.CityId.HasValue)
        {
            keyBuilder.Append($"city:{query.CityId}:");
        }

        if (query.Equipments is { Length: > 0 })
        {
            var equipmentsHash = ComputeHash(string.Join(",", query.Equipments.OrderBy(x => x)));
            keyBuilder.Append($"eq:{equipmentsHash}:");
        }

        if (query.Beans is { Length: > 0 })
        {
            var beansHash = ComputeHash(string.Join(",", query.Beans.OrderBy(x => x)));
            keyBuilder.Append($"beans:{beansHash}:");
        }

        if (query.Roasters is { Length: > 0 })
        {
            var roastersHash = ComputeHash(string.Join(",", query.Roasters.OrderBy(x => x)));
            keyBuilder.Append($"roasters:{roastersHash}:");
        }

        if (query.BrewMethods is { Length: > 0 })
        {
            var brewMethodsHash = ComputeHash(string.Join(",", query.BrewMethods.OrderBy(x => x)));
            keyBuilder.Append($"brew:{brewMethodsHash}:");
        }

        if (query.Tags is { Length: > 0 })
        {
            var tagsHash = ComputeHash(string.Join(",", query.Tags.OrderBy(x => x)));
            keyBuilder.Append($"tags:{tagsHash}:");
        }

        if (query.IsOpen.HasValue)
            keyBuilder.Append($"open:{query.IsOpen.Value}:");

        if (query.IsNew.HasValue)
            keyBuilder.Append($"new:{query.IsNew.Value}:");

        if (query.IsVisited.HasValue)
        {
            keyBuilder.Append($"visited:{query.IsVisited.Value}:");
            // Visited filter is user-specific — must include userId for cache correctness
            if (query.UserId.HasValue)
                keyBuilder.Append($"uid:{query.UserId.Value}:");
        }

        if (query.PriceRange.HasValue)
        {
            keyBuilder.Append($"price:{(int)query.PriceRange.Value}:");
        }

        if (query.Type.HasValue)
        {
            keyBuilder.Append($"type:{(int)query.Type.Value}:");
        }

        if (query.MinRating.HasValue)
        {
            keyBuilder.Append($"rating:{query.MinRating.Value}:");
        }

        keyBuilder.Append($"page:{query.PageNumber}:size:{query.PageSize}");

        return keyBuilder.ToString();
    }

    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash)[..16].Replace("/", "_").Replace("+", "-");
    }
}
