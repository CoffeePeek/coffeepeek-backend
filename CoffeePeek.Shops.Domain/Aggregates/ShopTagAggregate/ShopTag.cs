using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;

public sealed class ShopTag : Entity<Guid>, IPublicSlugOwner
{
    public string? PublicSlug { get; private set; }
    public long SlugRevision { get; private set; }

    public void SetPublicSlug(string slug)
    {
        CoffeePeek.Shared.Domain.PublicAddresses.PublicSlug.Validate(slug);
        if (PublicSlug == slug) return;
        PublicSlug = slug;
        SlugRevision = checked(SlugRevision + 1);
    }


    public string Slug { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private ShopTag()
    {
    }

    private ShopTag(Guid id, string slug, string name, string? description, int sortOrder)
    {
        Id = id;
        Slug = slug;
        Name = name;
        Description = description;
        SortOrder = sortOrder;
        IsActive = true;
    }

    public static ShopTag Create(string slug, string name, string? description = null, int sortOrder = 0)
    {
        var normalizedSlug = NormalizeSlug(slug);
        ValidateName(name);
        ValidateDescription(description);

        return new ShopTag(Guid.NewGuid(), normalizedSlug, name.Trim(), NormalizeDescription(description), sortOrder);
    }

    /// <summary>Used by EF seed / migrations with fixed IDs.</summary>
    public static ShopTag CreateWithId(
        Guid id,
        string slug,
        string name,
        string? description,
        int sortOrder,
        bool isActive = true)
    {
        var tag = new ShopTag(id, NormalizeSlug(slug), name.Trim(), NormalizeDescription(description), sortOrder)
        {
            IsActive = isActive
        };
        return tag;
    }

    public void Update(string name, string? description, int sortOrder, bool isActive)
    {
        ValidateName(name);
        ValidateDescription(description);

        Name = name.Trim();
        Description = NormalizeDescription(description);
        SortOrder = sortOrder;
        IsActive = isActive;
    }

    public void Deactivate() => IsActive = false;

    public static string NormalizeSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new DomainException("Slug is required.");

        var normalized = slug.Trim().ToLowerInvariant().Replace(' ', '_');

        if (normalized.Length > BusinessConstants.MaxShopTagSlugLength)
            throw new DomainException(
                $"Slug cannot be longer than {BusinessConstants.MaxShopTagSlugLength} characters.");

        return normalized;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Name is required.");

        if (name.Trim().Length > BusinessConstants.MaxShopTagNameLength)
            throw new DomainException(
                $"Name cannot be longer than {BusinessConstants.MaxShopTagNameLength} characters.");
    }

    private static void ValidateDescription(string? description)
    {
        if (description is null)
            return;

        if (description.Trim().Length > BusinessConstants.MaxShopTagDescriptionLength)
            throw new DomainException(
                $"Description cannot be longer than {BusinessConstants.MaxShopTagDescriptionLength} characters.");
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
