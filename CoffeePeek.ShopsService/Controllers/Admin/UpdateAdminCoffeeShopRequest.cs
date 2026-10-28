using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Enums;
using DomainCoffeeShopStatus = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShopStatus;

namespace CoffeePeek.ShopsService.Controllers.Admin;

public record UpdateAdminCoffeeShopRequest(
    string Name,
    string? Description,
    PriceRange PriceRange,
    DomainCoffeeShopStatus? Status,
    UpdateShopLocationRequest? Location = null,
    UpdateShopContactsRequest? Contacts = null,
    IReadOnlyList<ScheduleDto>? Schedules = null,
    UpdateShopCatalogsRequest? Catalogs = null);
