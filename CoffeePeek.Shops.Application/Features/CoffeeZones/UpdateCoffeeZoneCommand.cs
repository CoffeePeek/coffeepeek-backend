using System.Text.Json.Serialization;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public record UpdateCoffeeZoneCommand(
    [property: JsonIgnore] Guid Id,
    Guid CityId, string Name, string? Description, CoffeeZonePoint[] Polygon, string? Color = null);
