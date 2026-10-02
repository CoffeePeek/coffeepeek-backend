using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shared.Kernel.Drinks;

/// <summary>Catalog access implemented locally in Shops and over HTTP in Moderation.</summary>
public interface IDrinkCatalog
{
    Task<DrinkCatalogEntry?> FindAsync(string slug, CancellationToken ct);
}

public record DrinkCatalogEntry(string NameRu, string NameEn, bool IsActive);

/// <summary>A snapshot preserves the selected name when an admin renames or disables a drink.</summary>
public sealed record DrinkSelection(string Slug, string? CustomName, string NameRu, string NameEn)
{
    public const string OtherSlug = "other";
    public const int MaxNameLength = 100;
    public const int MaxSlugLength = 50;

    public static async Task<DrinkSelection?> ResolveAsync(
        string? slug, string? customName, IDrinkCatalog catalog, CancellationToken ct)
    {
        slug = slug?.Trim().ToLowerInvariant();
        customName = string.IsNullOrWhiteSpace(customName) ? null : customName.Trim();
        if (string.IsNullOrWhiteSpace(slug))
        {
            if (!string.IsNullOrWhiteSpace(customName))
                throw new ValidationException("Choose Other to specify a custom drink name.");
            return null;
        }
        if (slug.Length > MaxSlugLength)
            throw new ValidationException("Drink slug is too long.");
        if (slug == OtherSlug)
        {
            if (string.IsNullOrWhiteSpace(customName) || customName.Length > MaxNameLength)
                throw new ValidationException("Other requires a drink name of 1 to 100 characters.");
            return new(slug, customName, customName, customName);
        }
        if (!string.IsNullOrWhiteSpace(customName))
            throw new ValidationException("A custom drink name is only allowed for Other.");
        var drink = await catalog.FindAsync(slug, ct);
        if (drink is null || !drink.IsActive)
            throw new ValidationException("Drink not found or inactive.");
        return new(slug, null, drink.NameRu, drink.NameEn);
    }
}
