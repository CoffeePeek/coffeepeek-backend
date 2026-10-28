using CoffeePeek.Account.Application.Features.Admin.Stats;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Repositories;
using CoffeePeek.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class AccountLiveStatsTests : IClassFixture<AccountLiveDatabase>
{
    private readonly AccountLiveDatabase fixture;

    public AccountLiveStatsTests(AccountLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task Timeseries_CountsDistinctUsersPerUtcDayRegardlessOfDatabaseTimeZone()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.Context(false);
        await db.Database.OpenConnectionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SET TIME ZONE 'Europe/Kyiv'", ct);
        var role = await db.Roles.SingleAsync(r => r.Name == "User", ct);
        var from = DateTime.UtcNow.Date.AddDays(-2);
        var a = User.Register("dau-a@example.com", "dau_a", "hash", role);
        AddSession(a, from);
        AddSession(a, from.AddDays(1).AddMilliseconds(-1));
        AddSession(a, from.AddDays(1));
        a.RevokeAllSessions();
        var b = User.Register("dau-b@example.com", "dau_b", "hash", role);
        AddSession(b, from.AddDays(1).AddHours(23));
        var c = User.Register("dau-c@example.com", "dau_c", "hash", role);
        AddSession(c, from.AddMilliseconds(-1));
        db.Users.AddRange(a, b, c);
        var tokenDates = new[] { a, b, c }.SelectMany(u => u.RefreshTokens)
            .Select(t => new { t.Id, t.CreatedAtUtc }).ToArray();
        await db.SaveChangesAsync(ct);
        // AuditInterceptor stamps new rows, so backdate persisted tokens for this historical fixture.
        foreach (var token in tokenDates)
            await db.RefreshTokens.Where(t => t.Id == token.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.CreatedAtUtc, token.CreatedAtUtc), ct);

        var response = await GetAdminUsersTimeseriesHandler.Handle(
            new GetAdminUsersTimeseriesQuery(3), new AdminUserQueryRepository(db), ct);

        response.Data.DailyActiveUsers.Select(x => x.Count).Should().Equal(1, 2, 0);
        response.Data.DailyActiveUsers.Select(x => x.Date).Should()
            .Equal(response.Data.NewUsers.Select(x => x.Date));
    }

    private static void AddSession(User user, DateTime createdAtUtc)
    {
        user.AddSession(Guid.NewGuid().ToString("N"), TimeSpan.FromDays(-1), "device", "127.0.0.1");
        user.RefreshTokens.Last().CreatedAtUtc = createdAtUtc;
    }
}
