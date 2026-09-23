using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;

namespace CoffeePeek.Shops.Domain.Tests.Entities.CoffeeShopAggregate;

public class CityTests
{
    [Fact]
    public void Constructor_ValidName_SetsIdAndName()
    {
        var city = new City("Minsk");

        city.Id.Should().NotBeEmpty();
        city.Name.Should().Be("Minsk");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Constructor_BlankName_Throws(string invalidName)
    {
        var act = () => new City(invalidName);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Constructor_NameTooLong_Throws()
    {
        var name = new string('a', BusinessConstants.MaxCityNameLength + 1);

        var act = () => new City(name);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Update_ValidName_ChangesNameKeepsId()
    {
        var city = new City("Minsk");
        var originalId = city.Id;

        city.Update("Grodno");

        city.Id.Should().Be(originalId);
        city.Name.Should().Be("Grodno");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Update_BlankName_Throws(string invalidName)
    {
        var city = new City("Minsk");

        var act = () => city.Update(invalidName);

        act.Should().Throw<DomainException>();
        city.Name.Should().Be("Minsk");
    }

    [Fact]
    public void Update_NameTooLong_Throws()
    {
        var city = new City("Minsk");
        var name = new string('a', BusinessConstants.MaxCityNameLength + 1);

        var act = () => city.Update(name);

        act.Should().Throw<DomainException>();
        city.Name.Should().Be("Minsk");
    }
}
