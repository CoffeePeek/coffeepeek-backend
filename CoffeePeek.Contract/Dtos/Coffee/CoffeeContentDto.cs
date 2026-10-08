using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

public sealed record CoffeeContentDto(
    string Name, string? Description, string ProductKind, string ProductForm, string CompositionKind,
    string? Processing, string? RoastLevel, string? Acidity, string? Body, string? QGraderScoreRaw,
    string[] TasteDescriptors, string[] BrewRecommendations, string[] GrindOptions, string[] Features);
