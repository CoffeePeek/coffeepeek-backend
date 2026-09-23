using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

namespace CoffeePeek.Shops.Domain.Aggregates.BrewMethods;

public class BrewMethod : AggregateRoot<Guid>
{
    public string Name { get; private set; }
    public BrewMethodCategory Category { get; private set; }

    public IReadOnlyCollection<CoffeeShop>? CoffeeShops = new HashSet<CoffeeShop>();

    // ReSharper disable once UnusedMember.Local
    private BrewMethod() { }

    public BrewMethod(string name, BrewMethodCategory category)
    {
        ValidateName(name);

        Id = Guid.NewGuid();
        Name = name.Trim();
        Category = category;
    }

    public void Update(string name, BrewMethodCategory category)
    {
        ValidateName(name);

        Name = name.Trim();
        Category = category;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Name is required.");

        if (name.Trim().Length > BusinessConstants.MaxBrewMethodNameLength)
            throw new DomainException(
                $"Name cannot be longer than {BusinessConstants.MaxBrewMethodNameLength} characters.");
    }
}