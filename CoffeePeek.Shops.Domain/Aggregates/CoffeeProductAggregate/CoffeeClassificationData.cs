using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

public sealed record CoffeeClassificationData(
    string[] DefaultBrewPurposes, string? Caffeine, string? RoastLevel, string? Acidity,
    string[] Processing, string[] Fermentation, string[] TasteGroups, string? Composition);
