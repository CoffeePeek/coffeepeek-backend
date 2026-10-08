using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;

namespace CoffeePeek.Shops.Application.Features.Owner;

public record UpdateOwnerCoffeeShopCommand(
    Guid ShopId,
    Guid OwnerUserId,
    string Name,
    string? Description,
    string? PhoneNumber,
    string? Email,
    string? SiteLink,
    string? InstagramLink,
    ShopLocationPatch? Location,
    IReadOnlyList<ScheduleDto>? Schedules,
    ShopCatalogsPatch? Catalogs)
{
    public long? ExpectedRevision { get; init; }
}
