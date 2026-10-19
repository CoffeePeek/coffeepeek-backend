namespace CoffeePeek.Shops.Application.Abstractions;

public interface IMenuVisionParser
{
    Task<MenuVisionParseResult> ParseAsync(
        IReadOnlyList<MenuVisionPhoto> photos,
        CancellationToken ct = default);
}
