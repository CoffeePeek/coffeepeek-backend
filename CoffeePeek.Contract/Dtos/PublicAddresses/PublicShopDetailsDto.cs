using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Dtos.PublicAddresses;

public sealed record PublicShopDetailsDto : PublicShopSummaryDto
{
    public string? Description { get; init; }
    public PublicCheckInDto[] CheckIns { get; init; } = [];
    public PublicCheckInDto[] UserCheckIns { get; init; } = [];
    public List<ScheduleDto>? Schedules { get; init; }
    public PublicTagDto[] Tags { get; init; } = [];
    public ShopMenuDto? Menu { get; init; }
}
