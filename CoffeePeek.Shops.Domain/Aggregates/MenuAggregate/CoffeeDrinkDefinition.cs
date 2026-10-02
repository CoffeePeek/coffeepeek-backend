using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

public sealed class CoffeeDrinkDefinition : Entity<Guid>
{
    public string Slug { get; private set; } = null!;
    public string NameRu { get; private set; } = null!;
    public string NameEn { get; private set; } = null!;
    public CoffeeDrinkCategory Category { get; private set; }
    public CoffeeDrinkKind Kind { get; private set; }
    public string Aliases { get; private set; } = null!;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private CoffeeDrinkDefinition()
    {
    }

    private CoffeeDrinkDefinition(
        Guid id,
        string slug,
        string nameRu,
        string nameEn,
        CoffeeDrinkCategory category,
        string aliases,
        int sortOrder)
    {
        Id = id;
        Slug = slug;
        NameRu = nameRu;
        NameEn = nameEn;
        Category = category;
        Kind = CoffeeDrinkKind.Standard;
        Aliases = aliases;
        SortOrder = sortOrder;
        IsActive = true;
    }

    public static CoffeeDrinkDefinition CreateWithId(
        Guid id,
        string slug,
        string nameRu,
        string nameEn,
        CoffeeDrinkCategory category,
        string aliases,
        int sortOrder,
        bool isActive = true)
    {
        ValidateName(nameRu);
        ValidateName(nameEn);
        slug = slug?.Trim().ToLowerInvariant()!;
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > BusinessConstants.MaxCoffeeDrinkSlugLength ||
            slug == "other" || !System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9]+(?:[_-][a-z0-9]+)*$"))
            throw new DomainException("Slug must contain lowercase letters, digits, underscores or hyphens; other is reserved.");
        if (!Enum.IsDefined(category))
            throw new DomainException("Unknown drink category.");
        if (sortOrder < 0)
            throw new DomainException("Sort order cannot be negative.");
        if (aliases is null || aliases.Length > BusinessConstants.MaxCoffeeDrinkAliasesLength)
            throw new DomainException(
                $"Aliases cannot be longer than {BusinessConstants.MaxCoffeeDrinkAliasesLength} characters.");

        return new CoffeeDrinkDefinition(
            id,
            slug.Trim().ToLowerInvariant(),
            nameRu.Trim(),
            nameEn.Trim(),
            category,
            aliases.Trim(),
            sortOrder)
        {
            IsActive = isActive
        };
    }

    public void Update(string nameRu, string nameEn, CoffeeDrinkCategory category, string aliases, int sortOrder, bool isActive)
    {
        // Use the same validation for catalog creation and editing; the stable slug never changes.
        var validated = CreateWithId(Id, Slug, nameRu, nameEn, category, aliases, sortOrder, isActive);
        NameRu = validated.NameRu;
        NameEn = validated.NameEn;
        Category = validated.Category;
        Aliases = validated.Aliases;
        SortOrder = validated.SortOrder;
        IsActive = validated.IsActive;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public IReadOnlyList<string> AliasList =>
        Aliases.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Name is required.");

        if (name.Trim().Length > BusinessConstants.MaxCoffeeDrinkNameLength)
            throw new DomainException(
                $"Name cannot be longer than {BusinessConstants.MaxCoffeeDrinkNameLength} characters.");
    }
}
