using System.Text.Json.Serialization;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public record SetCoffeeZoneMembershipOverrideCommand(
    [property: JsonIgnore] Guid ZoneId,
    [property: JsonIgnore] Guid ShopId,
    CoffeeZoneMembershipOverrideKind Kind);
