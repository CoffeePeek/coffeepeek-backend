using System.ComponentModel.DataAnnotations;
using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public class City : Entity<Guid>
{
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