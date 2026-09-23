using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using FluentAssertions;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.CoffeeZoneAggregate;

public sealed class CoffeeZoneTests
{
    private static readonly CoffeeZonePoint[] Circle400 = CoffeeZonePoint.Circle(53.9m, 27.56m, 400);

    [Fact]
    public void Create_WithValidValues_StartsAsDraftAndTrimsContent()
    {
        var zone = new CoffeeZone(Guid.NewGuid(), "  Center  ", "  Guide  ", Circle400);

        zone.Status.Should().Be(CoffeeZoneStatus.Draft);
        zone.Name.Should().Be("Center");
        zone.Description.Should().Be("Guide");
    }


    [Fact]
    public void Update_CannotMoveZoneToAnotherCity()
    {
        var zone = new CoffeeZone(Guid.NewGuid(), "Center", null, Circle400);

        var act = () => zone.Update(Guid.NewGuid(), "Center", null, Circle400);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void StatusTransitions_AreExplicit()
    {
        var zone = new CoffeeZone(Guid.NewGuid(), "Center", null, Circle400);

        zone.Publish();
        zone.Status.Should().Be(CoffeeZoneStatus.Published);
        zone.Archive();
        zone.Status.Should().Be(CoffeeZoneStatus.Archived);
        zone.MoveToDraft();
        zone.Status.Should().Be(CoffeeZoneStatus.Draft);
    }

    // L-shape: the notch at (53.903, 27.565) is inside the bounding circle but outside the polygon.
    private static readonly CoffeeZonePoint[] LShape =
    [
        new(53.900m, 27.560m), new(53.904m, 27.560m), new(53.904m, 27.562m),
        new(53.902m, 27.562m), new(53.902m, 27.566m), new(53.900m, 27.566m)
    ];

    [Fact]
    public void Polygon_MembershipFollowsOutlineAndDerivesBoundingCircle()
    {
        var zone = new CoffeeZone(Guid.NewGuid(), "Center", null, LShape);

        zone.Contains(53.901m, 27.564m).Should().BeTrue();
        zone.Contains(53.903m, 27.561m).Should().BeTrue();
        zone.Contains(53.903m, 27.565m).Should().BeFalse();
        zone.Contains(53.910m, 27.560m).Should().BeFalse();
        zone.CenterLatitude.Should().Be(53.902m);
        LShape.Should().OnlyContain(p => CoffeePeek.Shops.Domain.GeoDistance.HaversineMeters(
            zone.CenterLatitude, zone.CenterLongitude, p.Latitude, p.Longitude) <= zone.RadiusMeters);
    }

    [Fact]
    public void Circle_StaysWithinRadiusAndHoldsPointsNearTheEdge()
    {
        var zone = new CoffeeZone(Guid.NewGuid(), "Center", null, CoffeeZonePoint.Circle(53.9m, 27.56m, 2000));

        zone.RadiusMeters.Should().BeLessThanOrEqualTo(2000);
        zone.Contains(53.9m, 27.56m).Should().BeTrue();
        zone.Contains(53.9m + 1_980m / 111_195m, 27.56m).Should().BeTrue();
        zone.Contains(53.9m + 2_010m / 111_195m, 27.56m).Should().BeFalse();
    }

    [Fact]
    public void Polygon_RejectsTooFewPointsAndOversizedOutline()
    {
        var tooFew = () => new CoffeeZone(Guid.NewGuid(), "Center", null,
            [new(53.9m, 27.56m), new(53.901m, 27.56m)]);
        var tooBig = () => new CoffeeZone(Guid.NewGuid(), "Center", null,
            [new(53.90m, 27.56m), new(53.95m, 27.56m), new(53.95m, 27.60m)]);

        var none = () => new CoffeeZone(Guid.NewGuid(), "Center", null, []);

        tooFew.Should().Throw<DomainException>();
        tooBig.Should().Throw<DomainException>();
        none.Should().Throw<DomainException>();
    }
}
