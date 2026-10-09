using System.Text.Json.Serialization;

namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public record DeleteCoffeeZoneMembershipOverrideCommand(
    [property: JsonIgnore] Guid ZoneId,
    [property: JsonIgnore] Guid ShopId);
