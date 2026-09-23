using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;

namespace CoffeePeek.Moderation.Domain.Import;

public readonly record struct ImportSuggestedTag(string Slug, string Why);

public readonly record struct ImportGaps(
    bool Instagram,
    bool Phone,
    bool Website,
    bool Hours,
    bool Photo);

public readonly record struct ImportYandexTagHint(
    string Label,
    string? Slug,
    ImportCoffeeFocus? Focus);

public static class ImportDossierAdvisor
{
    public static readonly IReadOnlyList<ImportYandexTagHint> YandexHints =
    [
        new("кофейня", null, ImportCoffeeFocus.Cafe),
        new("кафе", null, ImportCoffeeFocus.Cafe),
        new("Wi-Fi", "laptop_friendly", null),
        new("можно с ноутбуком", "laptop_friendly", null),
        new("завтраки", "bakery", null),
        new("кондитерская", "bakery", null),
        new("с собой", "to_go", null),
        new("обжарка", "roastery", null),
        new("спешелти", "specialty", ImportCoffeeFocus.Specialty)
    ];

    public static IReadOnlyList<ImportSuggestedTag> SuggestTags(
        IReadOnlyList<string> signals,
        string? cuisine)
    {
        var tags = new List<ImportSuggestedTag>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string slug, string why)
        {
            if (seen.Add(slug))
                tags.Add(new ImportSuggestedTag(slug, why));
        }

        foreach (var signal in signals)
        {
            switch (signal)
            {
                case "name:specialty-signal":
                    Add("specialty", "from name");
                    break;
                case "name:to-go-chain":
                    Add("to_go", "to-go chain");
                    break;
            }
        }

        var cuisineNorm = (cuisine ?? "").ToLowerInvariant();
        if (cuisineNorm.Contains("bakery")
            || cuisineNorm.Contains("pastry")
            || cuisineNorm.Contains("dessert")
            || cuisineNorm.Contains("cake")
            || cuisineNorm.Contains("кондитер")
            || cuisineNorm.Contains("пирож"))
            Add("bakery", "from cuisine");

        return tags;
    }

    public static ImportCoffeeFocus? SuggestFocus(
        IReadOnlyList<string> signals,
        ImportCollectorBucket bucket)
    {
        if (bucket == ImportCollectorBucket.LikelySpecialty
            || signals.Contains("name:specialty-signal"))
            return ImportCoffeeFocus.Specialty;

        if (signals.Contains("name:to-go-chain"))
            return ImportCoffeeFocus.CoffeeBar;

        if (signals.Contains("name:coffee")
            || signals.Contains("osm:shop=coffee")
            || signals.Any(s => s.StartsWith("osm:cuisine=", StringComparison.Ordinal)
                                && s.Contains("coffee", StringComparison.Ordinal)))
            return ImportCoffeeFocus.Cafe;

        if (signals.Contains("osm:amenity=cafe"))
            return ImportCoffeeFocus.Cafe;

        return null;
    }

    public static ImportGaps Gaps(string? instagram, string? phone, string? website, string? hours) =>
        new(
            Instagram: string.IsNullOrWhiteSpace(instagram),
            Phone: string.IsNullOrWhiteSpace(phone),
            Website: string.IsNullOrWhiteSpace(website),
            Hours: string.IsNullOrWhiteSpace(hours),
            Photo: true);
}
