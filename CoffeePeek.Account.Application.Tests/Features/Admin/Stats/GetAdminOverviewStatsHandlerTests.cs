using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Features.Admin.Stats;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.Admin.Stats;

public class GetAdminOverviewStatsHandlerTests
{
    private readonly Mock<IAdminUserQueryRepository> _users = new();
    private readonly Mock<IAdminStatsClient> _client = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Handle_WhenDownstreamIsDown_StillReturnsUserCountsAndZeros()
    {
        _users.Setup(r => r.GetStatsAsync(_ct)).ReturnsAsync(new AdminUserStats(
            TotalUsers: 12,
            ActiveUsers: 10,
            BlockedUsers: 2,
            RegisteredToday: 1,
            UsersByRole: new Dictionary<string, int>()));

        _client.Setup(c => c.GetPlatformStatsAsync(_ct)).ReturnsAsync(new AdminPlatformStatsSnapshot(
            TotalCoffeeShops: 0,
            TotalReviews: 0,
            NewCoffeeShopsToday: 0,
            NewReviewsToday: 0,
            PendingModerationShops: 0,
            PendingModerationReviews: 0,
            ImportPending: 0,
            ImportPublished: 0,
            ImportRejected: 0,
            ImportSkipped: 0,
            ImportInFeed: 0,
            ShopsAvailable: false,
            ModerationAvailable: false));

        var response = await GetAdminOverviewStatsHandler.Handle(
            new GetAdminOverviewStatsQuery(),
            _users.Object,
            _client.Object,
            _ct);

        response.IsSuccess.Should().BeTrue();
        response.Data.TotalUsers.Should().Be(12);
        response.Data.ActiveUsers.Should().Be(10);
        response.Data.BlockedUsers.Should().Be(2);
        response.Data.TotalCoffeeShops.Should().Be(0);
        response.Data.Import.Pending.Should().Be(0);
        response.Data.ShopsAvailable.Should().BeFalse();
        response.Data.ModerationAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenAllSourcesUp_MapsCatalogAndImportCounts()
    {
        _users.Setup(r => r.GetStatsAsync(_ct)).ReturnsAsync(new AdminUserStats(
            4, 3, 1, 0, new Dictionary<string, int>(), 0, 0, 0, 0, 0, 0));

        _client.Setup(c => c.GetPlatformStatsAsync(_ct)).ReturnsAsync(new AdminPlatformStatsSnapshot(
            TotalCoffeeShops: 40,
            TotalReviews: 9,
            NewCoffeeShopsToday: 2,
            NewReviewsToday: 1,
            PendingModerationShops: 3,
            PendingModerationReviews: 5,
            ImportPending: 120,
            ImportPublished: 8,
            ImportRejected: 4,
            ImportSkipped: 1,
            ImportInFeed: 5,
            ShopsAvailable: true,
            ModerationAvailable: true));

        var response = await GetAdminOverviewStatsHandler.Handle(
            new GetAdminOverviewStatsQuery(),
            _users.Object,
            _client.Object,
            _ct);

        response.IsSuccess.Should().BeTrue();
        response.Data.TotalCoffeeShops.Should().Be(40);
        response.Data.PendingModerationReviews.Should().Be(5);
        response.Data.Import.Pending.Should().Be(120);
        response.Data.Import.Published.Should().Be(8);
        response.Data.Import.InFeed.Should().Be(5);
        response.Data.ShopsAvailable.Should().BeTrue();
        response.Data.ModerationAvailable.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_MapsEngagementAndAccountBreakdownFields()
    {
        _users.Setup(r => r.GetStatsAsync(_ct)).ReturnsAsync(new AdminUserStats(
            TotalUsers: 100,
            ActiveUsers: 90,
            BlockedUsers: 4,
            RegisteredToday: 2,
            UsersByRole: new Dictionary<string, int>(),
            DeletedUsers: 6,
            DailyActiveUsers: 11,
            WeeklyActiveUsers: 33,
            MonthlyActiveUsers: 55,
            EmailConfirmedUsers: 70,
            GoogleUsers: 20));

        _client.Setup(c => c.GetPlatformStatsAsync(_ct)).ReturnsAsync(new AdminPlatformStatsSnapshot(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, true, true));

        var response = await GetAdminOverviewStatsHandler.Handle(
            new GetAdminOverviewStatsQuery(),
            _users.Object,
            _client.Object,
            _ct);

        response.Data.ActiveUsers.Should().Be(90);
        response.Data.BlockedUsers.Should().Be(4);
        response.Data.DeletedUsers.Should().Be(6);
        response.Data.DailyActiveUsers.Should().Be(11);
        response.Data.WeeklyActiveUsers.Should().Be(33);
        response.Data.MonthlyActiveUsers.Should().Be(55);
        response.Data.EmailConfirmedUsers.Should().Be(70);
        response.Data.GoogleUsers.Should().Be(20);
    }
}
