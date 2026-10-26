using CoffeePeek.Account.Application.Features.Leaderboards;
using CoffeePeek.Account.Infrastructure.Tests.Persistence;
using CoffeePeek.Contract.Events.Leaderboards;
using CoffeePeek.Shared.Kernel.Leaderboards;
using CoffeePeek.Testing;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class LeaderboardLiveTests(AccountLiveDatabase fixture) : IClassFixture<AccountLiveDatabase>
{
    [Fact]
    public async Task MigratedPostgresSupportsConcurrentUpsertRevocationAndRankedPaging()
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        var ct = TestContext.Current.CancellationToken;
        var user = await fixture.RegisterAsync("Leader" + Guid.NewGuid().ToString("N")[..8], true, ct);
        var date = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        var message = new ActivityScoreChanged(LeaderboardActivityKind.CheckIn, Guid.NewGuid(), user, 1, true, date);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            await using var concurrent = fixture.Context();
            await LeaderboardStoreTests.Scores(concurrent).ApplyAsync(message with { Version = i + 1, IsEligible = i != 7 }, ct);
        }));
        await using var db = fixture.Context(); var store = LeaderboardStoreTests.Scores(db);
        var row = await db.ActivityScores.AsNoTracking().SingleAsync(x => x.SourceId == message.SourceId, ct);
        Assert.Equal(8, row.Version); Assert.Equal(0, row.Points);
        await store.ApplyAsync(message with { Version = 9, CreditedAtUtc = date.AddMonths(1) }, ct);
        await store.ApplyAsync(new(LeaderboardActivityKind.ShopAdded, Guid.NewGuid(), user, 1, true, date), ct);
        var period = LeaderboardPeriod.Create("week", new(2026, 10, 26), date);
        var result = await store.GetAsync(period, 1, 1, user, ct);
        Assert.Equal(5, result.Me!.Points);
        Assert.Equal(1, result.Me.CheckIns); Assert.Equal(1, result.Me.Additions);
        Assert.Equal(1, result.Me.Rank);
        Assert.Single(result.Items);
        var nextMonth = await store.GetAsync(LeaderboardPeriod.Create("month", new(2026, 11, 1), date), 1, 20, user, ct);
        Assert.Null(nextMonth.Me);
    }
}
