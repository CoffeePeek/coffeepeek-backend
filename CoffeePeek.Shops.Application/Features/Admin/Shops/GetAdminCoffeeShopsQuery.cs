using DomainCoffeeShopStatus = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShopStatus;

namespace CoffeePeek.Shops.Application.Features.Admin.Shops;

public record GetAdminCoffeeShopsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    DomainCoffeeShopStatus? Status = null,
    bool? ImportedFromFile = null);
