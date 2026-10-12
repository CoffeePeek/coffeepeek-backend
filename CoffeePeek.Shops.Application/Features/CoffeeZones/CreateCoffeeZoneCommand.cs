using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public record CreateCoffeeZoneCommand(Guid CityId, string Name, string? Description, CoffeeZonePoint[] Polygon, string? Color = null);
