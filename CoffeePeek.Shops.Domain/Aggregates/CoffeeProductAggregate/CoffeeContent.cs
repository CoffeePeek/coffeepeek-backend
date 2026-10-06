using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

public sealed record CoffeeContent(
    string Name, string? Description, string ProductKind, string ProductForm, string CompositionKind,
    string? Processing, string? RoastLevel, string? Acidity, string? Body, string? QGraderScoreRaw,
    string[] TasteDescriptors, string[] BrewRecommendations, string[] GrindOptions, string[] Features);
