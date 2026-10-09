namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public sealed record CoffeeZoneMembershipPreviewDto(
    AdminCoffeeZoneDto Zone,
    CoffeeZoneMemberDto[] Members);
