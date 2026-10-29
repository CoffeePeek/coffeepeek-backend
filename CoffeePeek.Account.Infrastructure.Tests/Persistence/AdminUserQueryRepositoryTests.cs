using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Account.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Infrastructure.Tests.Persistence;

public class AdminUserQueryRepositoryTests
{
    private static AccountDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AccountDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static AccountDbContext CreateNpgsqlContext() =>
        new(new DbContextOptionsBuilder<AccountDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options);

    private readonly Role _userRole = Role.Create("User");

    private User NewUser(string name, DateTime? createdAtUtc = null)
    {
        var user = User.Register($"{name}@example.com", name, "hash", _userRole);
        user.CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        return user;
    }

    private static void AddTokenCreatedAgo(User user, TimeSpan ago) =>
        AddTokenCreatedAt(user, DateTime.UtcNow - ago);

    private static void AddTokenCreatedAt(User user, DateTime createdAtUtc)
    {
        user.AddSession(Guid.NewGuid().ToString("N"), TimeSpan.FromDays(60), "device", "127.0.0.1");
        user.RefreshTokens.Last().CreatedAtUtc = createdAtUtc;
    }

    [Fact]
    public void GetUsersAsync_SearchWithRole_TranslatesOnNpgsql()
    {
        using var db = CreateNpgsqlContext();
        var query = new AdminUserQueryRepository(db)
            .BuildUsersQuery("  AbC_%  ", "User");

        var countSql = query.GroupBy(_ => 1).Select(group => group.Count()).ToQueryString();
        var pageSql = query.OrderByDescending(user => user.CreatedAtUtc)
            .Skip(20).Take(20).ToQueryString();

        foreach (var sql in new[] { countSql, pageSql })
        {
            var commandSql = string.Join('\n', sql.Split('\n').Where(line => !line.StartsWith("--")));
            commandSql.Should().Contain("strpos(lower(\"Username\"), @p0)")
                .And.Contain("strpos(lower(\"Email\"), @p1)")
                .And.Contain("EXISTS")
                .And.Contain("\"Name\"")
                .And.NotContain("AbC_%")
                .And.NotContain("abc_%");
            sql.Should().Contain("abc_%"); // Parameter declaration in ToQueryString diagnostics.
        }
    }

    [Fact]
    public async Task GetStatsAsync_SeparatesBlockedDeletedAndActive()
    {
        await using var db = CreateContext();
        var active = NewUser("active");
        var blocked = NewUser("blocked");
        blocked.SetBlocked(true);
        var deleted = NewUser("deleted");
        deleted.SetSoftDelete();
        var blockedDeleted = NewUser("blockeddeleted");
        blockedDeleted.SetBlocked(true);
        blockedDeleted.SetSoftDelete();
        db.Users.AddRange(active, blocked, deleted, blockedDeleted);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var stats = await new AdminUserQueryRepository(db).GetStatsAsync(TestContext.Current.CancellationToken);

        stats.TotalUsers.Should().Be(4);
        stats.BlockedUsers.Should().Be(1);
        stats.DeletedUsers.Should().Be(2);
        stats.ActiveUsers.Should().Be(1);
    }

    [Fact]
    public async Task GetStatsAsync_CountsEmailConfirmedAndGoogleUsers()
    {
        await using var db = CreateContext();
        var unconfirmed = NewUser("unconfirmed");
        var confirmed = NewUser("confirmed");
        confirmed.ConfirmEmail(confirmed.Credentials.EmailConfirmationToken!);
        var google = User.CreateExternal("google@example.com", "Google", "google-sub-1");
        google.CreatedAtUtc = DateTime.UtcNow;
        var apple = User.CreateExternal("apple@example.com", "Apple", "apple-sub-1");
        var lowerGoogle = User.CreateExternal("lower@example.com", "google", "google-sub-2");
        var other = User.CreateExternal("other@example.com", "Other", "other-sub");
        db.Users.AddRange(unconfirmed, confirmed, google, apple, lowerGoogle, other);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var stats = await new AdminUserQueryRepository(db).GetStatsAsync(TestContext.Current.CancellationToken);

        stats.EmailConfirmedUsers.Should().Be(5);
        stats.GoogleUsers.Should().Be(2);
        stats.AppleUsers.Should().Be(1);
        stats.EmailUsers.Should().Be(2);
        stats.OtherAuthUsers.Should().Be(1);
        (stats.GoogleUsers + stats.AppleUsers + stats.EmailUsers + stats.OtherAuthUsers).Should().Be(stats.TotalUsers);
    }

    [Fact]
    public async Task GetStatsAsync_RetentionUsesExactCompleteUtcDaysAndDistinctUsers()
    {
        await using var db = CreateContext();
        var today = DateTime.UtcNow.Date;
        var returning = NewUser("returning", today.AddDays(-40).AddHours(23).AddMinutes(59));
        AddTokenCreatedAt(returning, returning.CreatedAtUtc); // Registration-day login is not a return.
        AddTokenCreatedAt(returning, today.AddDays(-39)); // D1 midnight, less than 24 hours later.
        AddTokenCreatedAt(returning, today.AddDays(-38).AddTicks(-1)); // Same D1 user, not another return.
        AddTokenCreatedAt(returning, today.AddDays(-33)); // D7.
        AddTokenCreatedAt(returning, today.AddDays(-10)); // D30.
        returning.RevokeAllSessions(); // Logout/revocation must not erase historical activity.
        var later = NewUser("later", today.AddDays(-40));
        AddTokenCreatedAt(later, today.AddDays(-38)); // D2 is not D1 or D7.
        later.SetSoftDelete(); // Include the same retained-account population as totalUsers.
        var week = NewUser("week", today.AddDays(-8));
        AddTokenCreatedAt(week, today.AddDays(-1));
        var month = NewUser("month", today.AddDays(-31));
        AddTokenCreatedAt(month, today.AddTicks(-1)); // Last instant of D30.
        var incompleteMonth = NewUser("incompletemonth", today.AddDays(-30));
        AddTokenCreatedAt(incompleteMonth, today); // D30 is still incomplete.
        var yesterday = NewUser("yesterday", today.AddDays(-1));
        AddTokenCreatedAt(yesterday, today); // D1 is still incomplete.
        var newToday = NewUser("newtoday", today);
        AddTokenCreatedAt(newToday, today);
        db.Users.AddRange(returning, later, week, month, incompleteMonth, yesterday, newToday);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var stats = await new AdminUserQueryRepository(db).GetStatsAsync(TestContext.Current.CancellationToken);

        stats.Retention.Should().NotBeNull();
        var retention = stats.Retention!;
        retention.AsOfDateUtc.Should().Be(DateOnly.FromDateTime(today));
        retention.D1.Should().Be(new AdminRetentionMetric(5, 1));
        retention.D1.RatePercent.Should().Be(20m);
        retention.D7.Should().Be(new AdminRetentionMetric(5, 2));
        retention.D30.Should().Be(new AdminRetentionMetric(3, 2));
        retention.D30.RatePercent.Should().Be(66.67m);
        retention.AfterRegistration.Should().Be(new AdminRetentionMetric(6, 4));
    }

    [Fact]
    public async Task GetStatsAsync_EmptyCohortsHaveNullRateRatherThanZeroRetention()
    {
        await using var db = CreateContext();
        db.Users.Add(NewUser("newtoday", DateTime.UtcNow.Date));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var retention = (await new AdminUserQueryRepository(db).GetStatsAsync(TestContext.Current.CancellationToken)).Retention!;
        foreach (var metric in new[] { retention.D1, retention.D7, retention.D30, retention.AfterRegistration })
        {
            metric.EligibleUsers.Should().Be(0);
            metric.ReturnedUsers.Should().Be(0);
            metric.RatePercent.Should().BeNull();
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(30)]
    public void RetentionQueries_TranslateToPostgresWithUtcBoundaries(int day)
    {
        using var db = CreateNpgsqlContext();
        var repository = new AdminUserQueryRepository(db);
        foreach (var query in new[] { repository.BuildDayRetainedUsersQuery(day, DateTime.UtcNow.Date),
                     repository.BuildReturnedAfterRegistrationQuery(DateTime.UtcNow.Date) })
        {
            var sql = query.GroupBy(_ => 1).Select(g => g.Count()).ToQueryString();
            sql.Should().Contain("EXISTS").And.Contain("AT TIME ZONE 'UTC'");
        }
    }

    [Fact]
    public async Task GetStatsAsync_CountsDistinctActiveUsersPerWindow()
    {
        await using var db = CreateContext();
        var a = NewUser("usera");
        AddTokenCreatedAgo(a, TimeSpan.FromHours(2));
        AddTokenCreatedAgo(a, TimeSpan.FromHours(3));
        var b = NewUser("userb");
        AddTokenCreatedAgo(b, TimeSpan.FromDays(3));
        var c = NewUser("userc");
        AddTokenCreatedAgo(c, TimeSpan.FromDays(20));
        var d = NewUser("userd");
        AddTokenCreatedAgo(d, TimeSpan.FromDays(40));
        db.Users.AddRange(a, b, c, d);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var stats = await new AdminUserQueryRepository(db).GetStatsAsync(TestContext.Current.CancellationToken);

        stats.DailyActiveUsers.Should().Be(1);
        stats.WeeklyActiveUsers.Should().Be(2);
        stats.MonthlyActiveUsers.Should().Be(3);
    }

    [Fact]
    public async Task GetDailyActiveUsersPerDayAsync_CountsDistinctUsersPerUtcDayFromStart()
    {
        await using var db = CreateContext();
        var from = new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);
        var a = NewUser("usera");
        AddTokenCreatedAt(a, from);
        AddTokenCreatedAt(a, from.AddDays(1).AddTicks(-1));
        AddTokenCreatedAt(a, from.AddDays(1));
        a.RevokeAllSessions();
        var b = NewUser("userb");
        b.AddSession("expired-token", TimeSpan.FromDays(-1), "device", "127.0.0.1");
        b.RefreshTokens.Last().CreatedAtUtc = from.AddDays(1).AddHours(2);
        var c = NewUser("userc");
        AddTokenCreatedAt(c, from.AddTicks(-1));
        db.Users.AddRange(a, b, c);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var perDay = await new AdminUserQueryRepository(db)
            .GetDailyActiveUsersPerDayAsync(from, TestContext.Current.CancellationToken);

        perDay.Should().BeEquivalentTo(new Dictionary<DateOnly, int>
        {
            [DateOnly.FromDateTime(from)] = 1,
            [DateOnly.FromDateTime(from.AddDays(1))] = 2
        });
    }

    [Fact]
    public async Task GetNewUsersPerDayAsync_GroupsByUtcDayFromStart()
    {
        await using var db = CreateContext();
        var today = DateTime.UtcNow.Date;
        db.Users.AddRange(
            NewUser("today1", today.AddHours(1)),
            NewUser("twoago1", today.AddDays(-2).AddHours(5)),
            NewUser("twoago2", today.AddDays(-2).AddHours(20)),
            NewUser("tooold", today.AddDays(-10)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var perDay = await new AdminUserQueryRepository(db)
            .GetNewUsersPerDayAsync(today.AddDays(-4), TestContext.Current.CancellationToken);

        perDay.Should().BeEquivalentTo(new Dictionary<DateOnly, int>
        {
            [DateOnly.FromDateTime(today)] = 1,
            [DateOnly.FromDateTime(today.AddDays(-2))] = 2
        });
    }
}
