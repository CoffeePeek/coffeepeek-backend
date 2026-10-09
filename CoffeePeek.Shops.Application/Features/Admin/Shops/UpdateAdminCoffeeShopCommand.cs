using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using DomainCoffeeShopStatus = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShopStatus;
using ContractPriceRange = CoffeePeek.Contract.Enums.PriceRange;

namespace CoffeePeek.Shops.Application.Features.Admin.Shops;

public record UpdateAdminCoffeeShopCommand(
    Guid ShopId,
    string Name,
    string? Description,
    ContractPriceRange PriceRange,
    DomainCoffeeShopStatus? Status,
    ShopLocationPatch? Location,
    ShopContactsPatch? Contacts,
    IReadOnlyList<ScheduleDto>? Schedules,
    ShopCatalogsPatch? Catalogs)
{
    public long? ExpectedRevision { get; init; }
}
