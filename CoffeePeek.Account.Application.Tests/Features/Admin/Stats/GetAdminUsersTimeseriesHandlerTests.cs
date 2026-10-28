using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Features.Admin.Stats;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.Admin.Stats;

public class GetAdminUsersTimeseriesHandlerTests
{
    private readonly Mock<IAdminUserQueryRepository> _users = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private void SetupCounts(IReadOnlyDictionary<DateOnly, int> counts,
        IReadOnlyDictionary<DateOnly, int> activeCounts = null)
    {
        _users.Setup(r => r.GetNewUsersPerDayAsync(It.IsAny<DateTime>(), _ct)).ReturnsAsync(counts);
        _users.Setup(r => r.GetDailyActiveUsersPerDayAsync(It.IsAny<DateTime>(), _ct))
            .ReturnsAsync(activeCounts ?? new Dictionary<DateOnly, int>());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(500, 90)]
    [InlineData(null, 30)]
    [InlineData(7, 7)]
    public async Task Handle_ClampsDays(int? requested, int expected)
    {
        SetupCounts(new Dictionary<DateOnly, int>());

        var response = await GetAdminUsersTimeseriesHandler.Handle(
            new GetAdminUsersTimeseriesQuery(requested), _users.Object, _ct);

        response.IsSuccess.Should().BeTrue();
        response.Data.Days.Should().Be(expected);
        response.Data.NewUsers.Should().HaveCount(expected);
        response.Data.DailyActiveUsers.Should().HaveCount(expected)
            .And.OnlyContain(x => x.Count == 0);
    }

    [Fact]
    public async Task Handle_ZeroFillsAscendingEndingToday()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        SetupCounts(new Dictionary<DateOnly, int> { [today.AddDays(-2)] = 5 },
            new Dictionary<DateOnly, int> { [today.AddDays(-2)] = 8, [today] = 3 });

        var response = await GetAdminUsersTimeseriesHandler.Handle(
            new GetAdminUsersTimeseriesQuery(3), _users.Object, _ct);

        response.Data.NewUsers.Select(x => x.Count).Should().Equal(5, 0, 0);
        response.Data.NewUsers.Select(x => x.Date).Should().Equal(today.AddDays(-2), today.AddDays(-1), today);
        response.Data.DailyActiveUsers.Select(x => x.Count).Should().Equal(8, 0, 3);
        response.Data.DailyActiveUsers.Select(x => x.Date).Should()
            .Equal(response.Data.NewUsers.Select(x => x.Date));
    }

    [Fact]
    public async Task Handle_QueriesFromStartOfWindow()
    {
        SetupCounts(new Dictionary<DateOnly, int>());

        await GetAdminUsersTimeseriesHandler.Handle(new GetAdminUsersTimeseriesQuery(3), _users.Object, _ct);

        var expectedFrom = DateTime.UtcNow.Date.AddDays(-2);
        _users.Verify(r => r.GetNewUsersPerDayAsync(expectedFrom, _ct), Times.Once);
        _users.Verify(r => r.GetDailyActiveUsersPerDayAsync(expectedFrom, _ct), Times.Once);
    }
}
