using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shops.Application.Features.Admin.Stats;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Stats;

public class GetAdminShopsTimeseriesHandlerTests
{
    private readonly Mock<IAdminStatsQueryRepository> _repository = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private static readonly IReadOnlyDictionary<DateOnly, int> Empty = new Dictionary<DateOnly, int>();

    [Theory]
    [InlineData(0, 1)]
    [InlineData(500, 90)]
    [InlineData(null, 30)]
    public async Task Handle_ClampsDays_AndReturnsEqualLengthSeries(int? requested, int expected)
    {
        _repository.Setup(r => r.GetDailyCountsAsync(It.IsAny<DateTime>(), _ct))
            .ReturnsAsync(new AdminShopsDailyCounts(Empty, Empty));

        var response = await GetAdminShopsTimeseriesHandler.Handle(
            new GetAdminShopsTimeseriesQuery(requested), _repository.Object, _ct);

        response.IsSuccess.Should().BeTrue();
        response.Data.Days.Should().Be(expected);
        response.Data.NewShops.Should().HaveCount(expected);
        response.Data.NewCheckIns.Should().HaveCount(expected);
    }

    [Fact]
    public async Task Handle_ZeroFillsEachSeriesEndingToday()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _repository.Setup(r => r.GetDailyCountsAsync(DateTime.UtcNow.Date.AddDays(-2), _ct))
            .ReturnsAsync(new AdminShopsDailyCounts(
                new Dictionary<DateOnly, int> { [today] = 2 },
                new Dictionary<DateOnly, int> { [today.AddDays(-1)] = 7 }));

        var response = await GetAdminShopsTimeseriesHandler.Handle(
            new GetAdminShopsTimeseriesQuery(3), _repository.Object, _ct);

        response.Data.NewShops.Select(x => x.Count).Should().Equal(0, 0, 2);
        response.Data.NewCheckIns.Select(x => x.Count).Should().Equal(0, 7, 0);
        response.Data.NewShops.Last().Date.Should().Be(today);
        response.Data.NewCheckIns.First().Date.Should().Be(today.AddDays(-2));
    }

    [Fact]
    public async Task Insights_RoundsAveragesAndMapsCollections()
    {
        var shopId = Guid.NewGuid();
        _repository.Setup(r => r.GetInsightsAsync(It.IsAny<DateTime>(), _ct))
            .ReturnsAsync(new AdminShopsInsights(
                new AdminRatingAggregate(3, 26m / 9m, 3m, 3m, 8m / 3m,
                    [(1, 1), (2, 0), (3, 1), (4, 0), (5, 1)]),
                [new AdminTopShop(shopId, "Alpha", 3)],
                new AdminDownloadAggregate(3, 2, [("android", 2, 2)], [("DE", 1), ("unknown", 1)]),
                new AdminShopCoverage(12, 43.755m, 4, 8)));

        var response = await GetAdminShopsInsightsHandler.Handle(
            new GetAdminShopsInsightsQuery(), _repository.Object, _ct);

        response.Data.Ratings.AverageRating.Should().Be(2.89m);
        response.Data.Ratings.AverageCoffee.Should().Be(2.67m);
        response.Data.Ratings.Distribution.Should().HaveCount(5);
        response.Data.TopShopsByCheckIns30Days.Single().Name.Should().Be("Alpha");
        response.Data.Downloads.Last30Days.Should().Be(2);
        response.Data.Downloads.TopCountries30Days.Select(c => c.Country).Should().Equal("DE", "unknown");
        response.Data.Coverage!.AverageCompletenessPercent.Should().Be(43.76m);
        response.Data.Coverage.TotalShops.Should().Be(12);
        response.Data.Coverage.ShopsWithCheckIns.Should().Be(4);
        response.Data.Coverage.ShopsWithoutCheckIns.Should().Be(8);
    }
}
