using System;
using System.Linq;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;
using CoffeePeek.Shops.Application.Features.CoffeeZones;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using FluentAssertions;

namespace CoffeePeek.Shops.Application.Tests.Features.CoffeeZones;

public sealed class CoffeeZoneSpatialTests
{
    [Fact]
    public void Membership_UsesDistanceAndOverridePrecedence()
    {
        var cityId = Guid.NewGuid();
        var zone = new CoffeeZone(cityId, "Center", null, 53.9000m, 27.5600m, 400);

        CoffeeZoneMembershipEvaluator.IsMember(cityId, 53.9005m, 27.5600m, zone, null).Should().BeTrue();
        CoffeeZoneMembershipEvaluator.IsMember(cityId, 53.9005m, 27.5600m, zone, CoffeeZoneMembershipOverrideKind.Exclude).Should().BeFalse();
        CoffeeZoneMembershipEvaluator.IsMember(cityId, 54.0000m, 27.5600m, zone, CoffeeZoneMembershipOverrideKind.Include).Should().BeTrue();
        CoffeeZoneMembershipEvaluator.IsMember(cityId, 54.0000m, 27.5600m, zone, CoffeeZoneMembershipOverrideKind.Primary).Should().BeTrue();
    }

    [Fact]
    public void Membership_RejectsAutomaticMembershipFromAnotherCity()
    {
        var zone = new CoffeeZone(Guid.NewGuid(), "Center", null, 53.9000m, 27.5600m, 400);

        CoffeeZoneMembershipEvaluator.IsMember(
            Guid.NewGuid(), 53.9000m, 27.5600m, zone, null).Should().BeFalse();
    }

    [Fact]
    public void MapClusters_AreStableWhenInputOrderChanges()
    {
        var points = new[]
        {
            new MapClusterPoint(Guid.Parse("00000000-0000-0000-0000-000000000001"), 53.9000m, 27.5600m),
            new MapClusterPoint(Guid.Parse("00000000-0000-0000-0000-000000000002"), 53.9001m, 27.5601m),
            new MapClusterPoint(Guid.Parse("00000000-0000-0000-0000-000000000003"), 53.9100m, 27.5800m)
        };

        var first = MapClusterBuilder.Build(points, 10, 60);
        var second = MapClusterBuilder.Build(points.Reverse(), 10, 60);

        second.Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
    }

    [Fact]
    public void MapClusters_LargeViewportGroupsPointsBeforeApplyingResponseLimit()
    {
        var points = Enumerable.Range(1, 20_000)
            .Select(index => new MapClusterPoint(
                Guid.Parse($"00000000-0000-0000-{index / 10_000:D4}-{index % 10_000:D12}"),
                53.8m + index % 200 * 0.0005m,
                27.4m + index % 250 * 0.0005m))
            .ToArray();

        var clusters = MapClusterBuilder.Build(points, 10, 60);

        clusters.Sum(cluster => cluster.Count).Should().Be(points.Length);
        clusters.Length.Should().BeLessThan(500);
    }

    [Fact]
    public void CandidateGeneration_FindsDenseGroupAndIsDeterministic()
    {
        var points = new[]
        {
            Point(1, 53.9000m, 27.5600m),
            Point(2, 53.9003m, 27.5600m),
            Point(3, 53.9000m, 27.5604m),
            Point(4, 53.9002m, 27.5603m),
            Point(5, 54.0000m, 28.0000m)
        };

        var first = CoffeeZoneCandidateGenerator.Generate(points, 400, 4);
        var second = CoffeeZoneCandidateGenerator.Generate(points.Reverse(), 400, 4);

        first.Should().ContainSingle();
        first[0].ShopCount.Should().Be(4);
        second.Should().BeEquivalentTo(first, options => options.WithStrictOrdering());
    }

    [Fact]
    public void CandidateGeneration_DoesNotReturnMembersOutsideAllowedZoneRadius()
    {
        var points = Enumerable.Range(0, 21)
            .Select(index => Point(index + 1, 53.9000m, 27.5600m + index * 0.004m))
            .ToArray();

        var candidate = CoffeeZoneCandidateGenerator.Generate(points, 400, 3).Single();

        candidate.SuggestedRadiusMeters.Should().BeLessThanOrEqualTo(2000);
        var allMembersFit = candidate.ShopIds.All(id =>
        {
            var point = points.Single(p => p.Id == id);
            return GeoDistance.HaversineMeters(
                candidate.CenterLatitude, candidate.CenterLongitude,
                point.Latitude, point.Longitude) <= candidate.SuggestedRadiusMeters;
        });
        allMembersFit.Should().BeTrue();
    }

    private static CoffeeZoneCandidatePoint Point(int suffix, decimal latitude, decimal longitude) =>
        new(Guid.Parse($"00000000-0000-0000-0000-{suffix:D12}"), latitude, longitude);
}
