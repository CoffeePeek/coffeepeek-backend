using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using FluentAssertions;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.CoffeeZoneAggregate;

public sealed class CoffeeZoneTests
{
    [Fact]
    public void Create_WithValidValues_StartsAsDraftAndTrimsContent()
    {
        var zone = new CoffeeZone(Guid.NewGuid(), "  Center  ", "  Guide  ", 53.9m, 27.56m, 400);

        zone.Status.Should().Be(CoffeeZoneStatus.Draft);
        zone.Name.Should().Be("Center");
        zone.Description.Should().Be("Guide");
    }

    [Theory]
    [InlineData(99)]
    [InlineData(2001)]
    public void Create_WithInvalidRadius_Throws(int radiusMeters)
    {
        var act = () => new CoffeeZone(Guid.NewGuid(), "Center", null, 53.9m, 27.56m, radiusMeters);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Update_CannotMoveZoneToAnotherCity()
    {
        var zone = new CoffeeZone(Guid.NewGuid(), "Center", null, 53.9m, 27.56m, 400);

        var act = () => zone.Update(Guid.NewGuid(), "Center", null, 53.9m, 27.56m, 400);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void StatusTransitions_AreExplicit()
    {
        var zone = new CoffeeZone(Guid.NewGuid(), "Center", null, 53.9m, 27.56m, 400);

        zone.Publish();
        zone.Status.Should().Be(CoffeeZoneStatus.Published);
        zone.Archive();
        zone.Status.Should().Be(CoffeeZoneStatus.Archived);
        zone.MoveToDraft();
        zone.Status.Should().Be(CoffeeZoneStatus.Draft);
    }
}
