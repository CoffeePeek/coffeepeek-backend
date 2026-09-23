using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using FluentAssertions;

namespace CoffeePeek.Shops.Domain.Tests.Entities.CoffeeShopAggregate;

public class RoasterTests
{
    [Fact]
    public void Constructor_ValidName_SetsIdAndName()
    {
        var roaster = new Roaster("Coffee Circus");

        roaster.Id.Should().NotBeEmpty();
        roaster.Name.Should().Be("Coffee Circus");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Constructor_BlankName_Throws(string invalidName)
    {
        var act = () => new Roaster(invalidName);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Constructor_NameTooLong_Throws()
    {
        var name = new string('a', BusinessConstants.MaxRoasterNameLength + 1);

        var act = () => new Roaster(name);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Update_ValidNameAndAbout_ChangesBothKeepsId()
    {
        var roaster = new Roaster("Coffee Circus");
        var originalId = roaster.Id;

        roaster.Update("Grunwald Coffee Roasters", "Small-batch specialty roaster.");

        roaster.Id.Should().Be(originalId);
        roaster.Name.Should().Be("Grunwald Coffee Roasters");
        roaster.About.Should().Be("Small-batch specialty roaster.");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Update_BlankName_Throws(string invalidName)
    {
        var roaster = new Roaster("Coffee Circus");

        var act = () => roaster.Update(invalidName, null);

        act.Should().Throw<DomainException>();
        roaster.Name.Should().Be("Coffee Circus");
    }

    [Fact]
    public void Update_NameTooLong_Throws()
    {
        var roaster = new Roaster("Coffee Circus");
        var name = new string('a', BusinessConstants.MaxRoasterNameLength + 1);

        var act = () => roaster.Update(name, null);

        act.Should().Throw<DomainException>();
        roaster.Name.Should().Be("Coffee Circus");
    }

    [Fact]
    public void Update_AboutTooLong_Throws()
    {
        var roaster = new Roaster("Coffee Circus");
        var about = new string('a', BusinessConstants.MaxRoasterAboutLength + 1);

        var act = () => roaster.Update("Coffee Circus", about);

        act.Should().Throw<DomainException>();
        roaster.About.Should().BeNull();
    }

    [Fact]
    public void SetLocation_SetsLocation()
    {
        var roaster = new Roaster("Coffee Circus");
        var location = Location.CreateValidated(Guid.NewGuid(), "1 Main St", 53.9m, 27.5m);

        roaster.SetLocation(location);

        roaster.Location.Should().Be(location);
    }

    [Fact]
    public void SetContact_SetsContact()
    {
        var roaster = new Roaster("Coffee Circus");
        var contact = RoasterContact.Create("https://instagram.com/roaster", "https://roaster.com");

        roaster.SetContact(contact);

        roaster.Contact.Should().Be(contact);
    }

    [Fact]
    public void ReplacePhotos_ReplacesExistingPhotos()
    {
        var roaster = new Roaster("Coffee Circus");
        roaster.ReplacePhotos([new RoasterPhoto("a.jpg", "image/jpeg", "key-a", 100, Guid.NewGuid())]);

        roaster.ReplacePhotos([new RoasterPhoto("b.jpg", "image/jpeg", "key-b", 200, Guid.NewGuid())]);

        roaster.Photos.Should().ContainSingle(p => p.FileName == "b.jpg");
    }
}
