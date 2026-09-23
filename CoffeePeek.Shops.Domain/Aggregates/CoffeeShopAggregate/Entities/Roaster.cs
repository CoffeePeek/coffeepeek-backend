using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public class Roaster : Entity<Guid>
{
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

    public Roaster(string name, Guid? moderationId = null)
    {
        ValidateName(name);

        Id = Guid.NewGuid();
        Name = name.Trim();
        ModerationId = moderationId;
    }

    public void Update(string name, string? about)
    {
        ValidateName(name);
        ValidateAbout(about);

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
        _photos.Clear();
        _photos.AddRange(photos);
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