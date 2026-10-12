using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public class Roaster : Entity<Guid>, IPublicSlugOwner
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


    public string Name { get; private set; }
    public string? About { get; private set; }
    public Location? Location { get; private set; }
    public RoasterContact? Contact { get; private set; }
    public Guid? ModerationId { get; private set; }
    public ICollection<CoffeeShop> CoffeeShops { get; private set; } = new HashSet<CoffeeShop>();

    private readonly List<RoasterPhoto> _photos = [];
    public IReadOnlyCollection<RoasterPhoto> Photos => _photos.AsReadOnly();

    // ReSharper disable once UnusedMember.Local
    private Roaster() { }

    public Roaster(string name, Guid? moderationId = null, Guid? id = null)
    {
        ValidateName(name);
        if (id == Guid.Empty) throw new DomainException("Roaster ID is required.");

        Id = id ?? Guid.NewGuid();
        Name = name.Trim();
        ModerationId = moderationId;
    }

    public void Update(string name, string? about)
    {
        ValidateDetails(name, about);

        Name = name.Trim();
        About = about?.Trim();
    }

    public void SetAbout(string? about)
    {
        ValidateAbout(about);
        About = about?.Trim();
    }

    public void SetLocation(Location? location)
    {
        Location = location;
    }

    public void SetContact(RoasterContact? contact)
    {
        Contact = contact;
    }

    public void ReplacePhotos(IEnumerable<RoasterPhoto> photos)
    {
        ArgumentNullException.ThrowIfNull(photos);
        var replacements = photos.ToArray();
        if (replacements.Length > 20 || replacements.Any(p => p == null)
            || replacements.Select(p => p.Id).Distinct().Count() != replacements.Length
            || replacements.Select(p => p.StorageKey).Distinct(StringComparer.Ordinal).Count() != replacements.Length)
            throw new DomainException("Roaster photos must be distinct; at most twenty are allowed.");
        _photos.Clear();
        _photos.AddRange(replacements);
    }

    public static void ValidateDetails(string name, string? about)
    {
        ValidateName(name);
        ValidateAbout(about);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Name is required.");

        if (name.Trim().Length > BusinessConstants.MaxRoasterNameLength)
            throw new DomainException(
                $"Name cannot be longer than {BusinessConstants.MaxRoasterNameLength} characters.");
    }

    private static void ValidateAbout(string? about)
    {
        if (about != null && about.Trim().Length > BusinessConstants.MaxRoasterAboutLength)
            throw new DomainException(
                $"About cannot be longer than {BusinessConstants.MaxRoasterAboutLength} characters.");
    }
}
