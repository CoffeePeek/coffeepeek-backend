using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Persistance.Discovery;

public sealed partial class DiscoveryQueryStore
{
    public async Task<DiscoverySearchResult> SearchDiscoveryAsync(DiscoverySearchRequest request, Guid? userId, CancellationToken ct)
    {
        var filters = request.Filters ?? new();
        var budget = filters.Budget;
        var menuCriteria = filters.Brew.Length > 0 || budget?.MinDrinkPrice.HasValue == true ||
            budget?.MaxDrinkPrice.HasValue == true;
        var result = new DiscoverySearchResult { Q = NormalizeQuery(request.Q) };
        if (request.Sections.Contains("coffee_shops")) result.CoffeeShops = await SearchShopsAsync(new()
        {
            Q = request.Q, Sort = request.Sort, Page = request.CoffeeShops?.Page ?? 1, PageSize = request.CoffeeShops?.PageSize ?? 10,
            Filters = new() { Menu = menuCriteria ? new MenuFilters
            {
                Brew = filters.Brew, Currency = budget?.Currency, MinPrice = budget?.MinDrinkPrice,
                MaxPrice = budget?.MaxDrinkPrice
            } : null }
        }, userId, ct);
        return result;
    }
}
