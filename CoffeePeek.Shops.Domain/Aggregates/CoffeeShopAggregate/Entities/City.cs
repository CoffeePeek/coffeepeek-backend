using CoffeePeek.Shared.Domain.PublicAddresses;
using System.ComponentModel.DataAnnotations;
using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public class City : Entity<Guid>, IPublicSlugOwner
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


    [MaxLength(BusinessConstants.MaxCityNameLength)]
    public string Name { get; private set; }

    public City(string name)
    {
        ValidateName(name);

        Id = Guid.NewGuid();
        Name = name.Trim();
    }

    public void Update(string name)
    {
        ValidateName(name);

        Name = name.Trim();
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Name is required.");

        if (name.Trim().Length > BusinessConstants.MaxCityNameLength)
            throw new DomainException($"Name cannot be longer than {BusinessConstants.MaxCityNameLength} characters.");
    }
}