using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;

namespace CoffeePeek.Shops.Domain.Tests.Entities.CoffeeShopAggregate;

public class CoffeeBeanTests
{
    [Fact]
    public void Constructor_ValidName_SetsIdAndName()
    {
        var bean = new CoffeeBean("Arabica");

        bean.Id.Should().NotBeEmpty();
        bean.Name.Should().Be("Arabica");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Constructor_BlankName_Throws(string invalidName)
    {
        var act = () => new CoffeeBean(invalidName);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Constructor_NameTooLong_Throws()
    {
        var name = new string('a', BusinessConstants.MaxCoffeeBeanNameLength + 1);

        var act = () => new CoffeeBean(name);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Update_ValidName_ChangesNameKeepsId()
    {
        var bean = new CoffeeBean("Arabica");
        var originalId = bean.Id;

        bean.Update("Robusta");

        bean.Id.Should().Be(originalId);
        bean.Name.Should().Be("Robusta");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Update_BlankName_Throws(string invalidName)
    {
        var bean = new CoffeeBean("Arabica");

        var act = () => bean.Update(invalidName);

        act.Should().Throw<DomainException>();
        bean.Name.Should().Be("Arabica");
    }

    [Fact]
    public void Update_NameTooLong_Throws()
    {
        var bean = new CoffeeBean("Arabica");
        var name = new string('a', BusinessConstants.MaxCoffeeBeanNameLength + 1);

        var act = () => bean.Update(name);

        act.Should().Throw<DomainException>();
        bean.Name.Should().Be("Arabica");
    }
}
