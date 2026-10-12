using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.CoffeeShopAggregate;

public class CoffeeShopProfileInvariantsTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    public void Constructor_RejectsBlankName(string name)
    {
        var create = () => new CoffeeShop(null, name, null, PriceRange.Cheap, Guid.NewGuid());

        create.Should().Throw<DomainException>().WithMessage("*name*");
    }

    [Fact]
    public void Constructor_RejectsNullName()
    {
        var create = () => new CoffeeShop(null, null!, null, PriceRange.Cheap, Guid.NewGuid());

        create.Should().Throw<DomainException>().WithMessage("*name*");
    }

    [Fact]
    public void Constructor_AcceptsExistingFieldLengthLimits()
    {
        var shop = new CoffeeShop(null,
            new string('n', BusinessConstants.MaxCoffeeShopNameLength),
            new string('d', BusinessConstants.MaxCoffeeShopDescriptionLength),
            PriceRange.Expensive, Guid.NewGuid());

        shop.Name.Should().HaveLength(BusinessConstants.MaxCoffeeShopNameLength);
        shop.Description.Should().HaveLength(BusinessConstants.MaxCoffeeShopDescriptionLength);
    }

    [Fact]
    public void Constructor_RejectsInvalidPriceRange()
    {
        var create = () => new CoffeeShop(null, "Shop", null, (PriceRange)99, Guid.NewGuid());

        create.Should().Throw<DomainException>().WithMessage("*price range*");
    }

    [Fact]
    public void UpdateDetails_RejectsLateInvalidFieldWithoutChangingEarlierFields()
    {
        var shop = NewShop();
        shop.UpdateDetails("Original", "Original description", PriceRange.Moderate);

        var update = () => shop.UpdateDetails("Changed", new string('x', BusinessConstants.MaxCoffeeShopDescriptionLength + 1), PriceRange.Expensive);

        update.Should().Throw<DomainException>().WithMessage("*description*");
        shop.Name.Should().Be("Original");
        shop.Description.Should().Be("Original description");
        shop.PriceRange.Should().Be(PriceRange.Moderate);
    }

    [Fact]
    public void UpdateDetails_RejectsOverlongNameWithoutChangingProfile()
    {
        var shop = NewShop();

        var update = () => shop.UpdateDetails(new string('x', BusinessConstants.MaxCoffeeShopNameLength + 1), "Changed", PriceRange.Expensive);

        update.Should().Throw<DomainException>().WithMessage("*name*");
        shop.Name.Should().Be("Original");
        shop.Description.Should().BeNull();
        shop.PriceRange.Should().Be(PriceRange.Cheap);
    }

    [Fact]
    public void InvalidEnumMutators_LeavePreviousValuesIntact()
    {
        var shop = NewShop();
        shop.SetCoffeeFocus(CoffeeFocusType.Specialty);

        var setPrice = () => shop.SetPriceRange((PriceRange)99);
        var setStatus = () => shop.SetStatus((CoffeeShopStatus)99);
        var setFocus = () => shop.SetCoffeeFocus((CoffeeFocusType)99);

        setPrice.Should().Throw<DomainException>();
        setStatus.Should().Throw<DomainException>();
        setFocus.Should().Throw<DomainException>();
        shop.PriceRange.Should().Be(PriceRange.Cheap);
        shop.Status.Should().Be(CoffeeShopStatus.Active);
        shop.Type.Should().Be(CoffeeFocusType.Specialty);
    }

    [Fact]
    public void SetCoffeeFocus_AllowsUnknownFocus()
    {
        var shop = NewShop();
        shop.SetCoffeeFocus(CoffeeFocusType.Specialty);

        shop.SetCoffeeFocus(null);

        shop.Type.Should().BeNull();
    }

    private static CoffeeShop NewShop() =>
        new(null, "Original", null, PriceRange.Cheap, Guid.NewGuid());
}
