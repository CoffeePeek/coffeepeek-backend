using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public class CoffeeBean : Entity<Guid>
{
    public string Name { get; private set; }

    public IReadOnlyCollection<CoffeeShop> CoffeeShops { get; private set; } = new HashSet<CoffeeShop>();

    // ReSharper disable once UnusedMember.Local
    private CoffeeBean() { }

    public CoffeeBean(string name)
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

        if (name.Trim().Length > BusinessConstants.MaxCoffeeBeanNameLength)
            throw new DomainException(
                $"Name cannot be longer than {BusinessConstants.MaxCoffeeBeanNameLength} characters.");
    }
}