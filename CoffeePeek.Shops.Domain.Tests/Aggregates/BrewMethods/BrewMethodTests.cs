using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using FluentAssertions;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.BrewMethods;

public class BrewMethodTests
{
    [Fact]
    public void Constructor_ValidNameAndCategory_SetsIdNameAndCategory()
    {
        var brewMethod = new BrewMethod("V60", BrewMethodCategory.Gravity);

        brewMethod.Id.Should().NotBeEmpty();
        brewMethod.Name.Should().Be("V60");
        brewMethod.Category.Should().Be(BrewMethodCategory.Gravity);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Constructor_BlankName_Throws(string invalidName)
    {
        var act = () => new BrewMethod(invalidName, BrewMethodCategory.Gravity);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Constructor_NameTooLong_Throws()
    {
        var name = new string('a', BusinessConstants.MaxBrewMethodNameLength + 1);

        var act = () => new BrewMethod(name, BrewMethodCategory.Gravity);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Update_ValidNameAndCategory_ChangesBothKeepsId()
    {
        var brewMethod = new BrewMethod("V60", BrewMethodCategory.Gravity);
        var originalId = brewMethod.Id;

        brewMethod.Update("Chemex", BrewMethodCategory.Gravity);

        brewMethod.Id.Should().Be(originalId);
        brewMethod.Name.Should().Be("Chemex");
        brewMethod.Category.Should().Be(BrewMethodCategory.Gravity);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Update_BlankName_Throws(string invalidName)
    {
        var brewMethod = new BrewMethod("V60", BrewMethodCategory.Gravity);

        var act = () => brewMethod.Update(invalidName, BrewMethodCategory.Gravity);

        act.Should().Throw<DomainException>();
        brewMethod.Name.Should().Be("V60");
    }

    [Fact]
    public void Update_NameTooLong_Throws()
    {
        var brewMethod = new BrewMethod("V60", BrewMethodCategory.Gravity);
        var name = new string('a', BusinessConstants.MaxBrewMethodNameLength + 1);

        var act = () => brewMethod.Update(name, BrewMethodCategory.Gravity);

        act.Should().Throw<DomainException>();
        brewMethod.Name.Should().Be("V60");
    }
}
