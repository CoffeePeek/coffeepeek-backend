using System.Text.Json.Serialization;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public record SetCoffeeZoneStatusCommand([property: JsonIgnore] Guid Id, CoffeeZoneStatus Status);
