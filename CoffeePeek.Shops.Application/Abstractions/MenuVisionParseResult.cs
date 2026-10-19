namespace CoffeePeek.Shops.Application.Abstractions;

public record MenuVisionParseResult(
    bool Success,
    string? Error,
    IReadOnlyList<VisionDrinkLine> Drinks);
