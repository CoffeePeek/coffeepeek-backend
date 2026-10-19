using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ContractCategory = CoffeePeek.Contract.Enums.CoffeeDrinkCategory;
using ContractPriceRange = CoffeePeek.Contract.Enums.PriceRange;

namespace CoffeePeek.Shops.Application.Features.Menu.ParseMenuPhotos;

public static class ParseMenuPhotosHandler
{
    public static async Task<Response<ParseMenuPhotosResponse>> Handle(
        ParseMenuPhotosCommand command,
        IMenuVisionParser parser,
        IQueryCoffeeDrinkRepository drinkRepository,
        IMenuPhotoDownloader photoDownloader,
        IOptions<MenuPriceRangeOptions> priceOptions,
        ILogger logger,
        CancellationToken ct)
    {
        var photos = command.Photos
            .Where(p => !string.IsNullOrWhiteSpace(p.PublicUrl))
            .Take(BusinessConstants.MaxMenuPhotosPerParse)
            .Select(p => new MenuPhotoDownloadRequest(p.PublicUrl!, p.ContentType))
            .ToArray();

        if (photos.Length == 0)
        {
            logger.LogWarning("Menu parse has no public photo URLs");
            return Response<ParseMenuPhotosResponse>.Success(Failed("No menu photo URLs to parse."));
        }

        var downloaded = await photoDownloader.DownloadAsync(photos, ct);

        if (downloaded.Count == 0)
        {
            logger.LogError("Menu parse could not download any of {Count} photos", photos.Length);
            return Response<ParseMenuPhotosResponse>.Success(Failed("Could not download menu photos."));
        }

        var vision = await parser.ParseAsync(downloaded, ct);
        if (!vision.Success)
        {
            logger.LogError("Menu vision parse failed: {Error}", vision.Error ?? "Parse failed.");
            return Response<ParseMenuPhotosResponse>.Success(Failed(vision.Error ?? "Parse failed."));
        }

        var catalog = await drinkRepository.GetActiveAsync(ct);
        var matched = new Dictionary<string, ParsedMenuItemDto>(StringComparer.OrdinalIgnoreCase);
        var unmatched = new List<UnmatchedMenuItemDto>();

        foreach (var line in Sanitize(vision.Drinks))
        {
            var drink = MenuDrinkMatcher.Match(catalog, line.RawName);
            if (drink is null)
            {
                unmatched.Add(new UnmatchedMenuItemDto(line.RawName, line.Price, line.Confidence));
                continue;
            }

            if (matched.TryGetValue(drink.Slug, out var existing))
            {
                if (line.Price!.Value < existing.Price!.Value)
                    matched[drink.Slug] = Matched(drink, line);
                continue;
            }

            matched[drink.Slug] = Matched(drink, line);
        }

        var items = matched.Values.ToArray();
        var settings = priceOptions.Value;
        var suggested = MenuPriceRangeCalculator.FromPrices(
            items.Where(i => i.Price.HasValue).Select(i => i.Price!.Value),
            settings.CheapBelow,
            settings.ExpensiveAbove);

        logger.LogInformation(
            "Menu parse matched {ItemCount} drinks, {UnmatchedCount} unmatched, suggested={Suggested}",
            items.Length,
            unmatched.Count,
            suggested);

        return Response<ParseMenuPhotosResponse>.Success(new ParseMenuPhotosResponse(
            true,
            null,
            suggested is null ? null : (ContractPriceRange)(int)suggested.Value,
            items,
            unmatched));
    }

    // The JSON schema fixes the shape, not the values: text in a menu photo can steer the model into absurd
    // numbers, which skewed the price range, overflowed numeric(10,2) and bloated UnmatchedJson.
    private const int MaxDrinkLines = 200;
    private const int MaxRawNameLength = 100;
    private const decimal MaxPrice = 10_000m;

    public static IEnumerable<VisionDrinkLine> Sanitize(IEnumerable<VisionDrinkLine> lines) =>
        lines
            .Where(l => !string.IsNullOrWhiteSpace(l.RawName))
            .Take(MaxDrinkLines)
            .Select(l => l with
            {
                RawName = l.RawName.Length > MaxRawNameLength ? l.RawName[..MaxRawNameLength] : l.RawName,
                Price = l.Price is > 0 and <= MaxPrice ? l.Price : null,
                Confidence = l.Confidence is { } c ? Math.Clamp(c, 0, 1) : null
            })
            // A partially known OCR offer cannot become a Present domain item.
            .Where(l => l.Price.HasValue);

    private static ParseMenuPhotosResponse Failed(string error) =>
        new(false, error, null, [], []);

    private static ParsedMenuItemDto Matched(CoffeeDrinkDefinition drink, VisionDrinkLine line) => new(
        drink.Slug,
        line.Price,
        line.RawName,
        drink.NameRu,
        drink.NameEn,
        (ContractCategory)(int)drink.Category);
}
