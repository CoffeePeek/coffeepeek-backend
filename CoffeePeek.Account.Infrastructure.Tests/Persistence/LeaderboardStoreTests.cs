using CoffeePeek.Account.Application.Features.Leaderboards;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Persistence.Leaderboards;
using CoffeePeek.Contract.Events.Leaderboards;
using CoffeePeek.Shared.Kernel.Leaderboards;
using CoffeePeek.Shared.Kernel.Options;
using Microsoft.EntityFrameworkCore;
using static CoffeePeek.Account.Infrastructure.Tests.Persistence.ProfileSocialStoreTests;

namespace CoffeePeek.Account.Infrastructure.Tests.Persistence;

public sealed class LeaderboardStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    internal static LeaderboardStore Scores(CoffeePeek.Account.Persistence.Configuration.AccountDbContext db) => new(db,
        Microsoft.Extensions.Options.Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com" }));
    private static readonly DateTime Date = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
    private static readonly LeaderboardPeriod Week = LeaderboardPeriod.Create("week", new(2026, 10, 26), Date);

    [Theory]
    [InlineData("week", "2027-01-03", "2026-12-28", "2027-01-04")]
    [InlineData("week", "2026-10-26", "2026-10-26", "2026-11-02")]
    [InlineData("month", "2028-02-15", "2028-02-01", "2028-03-01")]
    public void PeriodUsesCalendarBoundaries(string kind, string date, string start, string end)
    {
        var period = LeaderboardPeriod.Create(kind, DateOnly.Parse(date), Date);
        Assert.Equal(DateOnly.Parse(start), period.StartsOn);
        Assert.Equal(DateOnly.Parse(end), period.EndsOnExclusive);
        Assert.Equal(period.StartsOn.ToDateTime(TimeOnly.MinValue).AddHours(-3), period.StartsAtUtc);
        Assert.Equal(DateTimeKind.Utc, period.StartsAtUtc.Kind);
        var localMonday = LeaderboardPeriod.Create("week", null, new DateTimeOffset(2026, 10, 25, 21, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 10, 26), localMonday.StartsOn);
    }

    [Fact]
    public async Task WeightsTiesPagingAndEligibilityUseCurrentUserState()
    {
        await using var db = Db(); var store = Scores(db);
        var alice = NewUser("Alice"); var bob = NewUser("Bob"); var low = NewUser("Low");
        var admin = NewUser("Admin"); admin.ReplaceRoles(Role.Create("Admin"));
        var moderator = NewUser("Moderator"); moderator.ReplaceRoles(Role.Create("Moderator"));
        var blocked = NewUser("Blocked"); blocked.SetBlocked(true);
        db.AddRange(alice, bob, low, admin, moderator, blocked); await db.SaveChangesAsync(Ct);
        async Task Grant(User user, LeaderboardActivityKind kind) =>
            await store.ApplyAsync(new(kind, Guid.NewGuid(), user.Id, 1, true, Date), Ct);
        await Grant(alice, LeaderboardActivityKind.ShopAdded);
        await Grant(alice, LeaderboardActivityKind.ShopEdited);
        await Grant(alice, LeaderboardActivityKind.CheckIn);
        await Grant(bob, LeaderboardActivityKind.RoasterAdded);
        await Grant(bob, LeaderboardActivityKind.ShopAdded);
        await Grant(low, LeaderboardActivityKind.CheckIn);
        foreach (var user in new[] { admin, moderator, blocked }) await Grant(user, LeaderboardActivityKind.ShopAdded);
        var first = await store.GetAsync(Week, 1, 2, low.Id, Ct);
        Assert.Equal(3, first.TotalCount);
        Assert.All(first.Items, x => { Assert.Equal(6, x.Points); Assert.Equal(1, x.Rank); });
        Assert.Equal(3, first.Me!.Rank);
        var second = await store.GetAsync(Week, 2, 2, alice.Id, Ct);
        Assert.Equal(3, Assert.Single(second.Items).Rank);
        Assert.Equal(1, second.Me!.Additions); Assert.Equal(1, second.Me.Edits); Assert.Equal(1, second.Me.CheckIns);
        alice.ReplaceRoles(admin.Roles.Single()); await db.SaveChangesAsync(Ct);
        Assert.Equal(2, (await store.GetAsync(Week, 1, 20, alice.Id, Ct)).TotalCount);
        Assert.Null((await store.GetAsync(Week, 1, 20, alice.Id, Ct)).Me);
    }

    [Fact]
    public async Task ReplayOldMessagesRevocationAndRestorationCannotDuplicateOrMoveCredits()
    {
        await using var db = Db(); var store = Scores(db); var user = NewUser("Author");
        db.Add(user); await db.SaveChangesAsync(Ct);
        var grant = new ActivityScoreChanged(LeaderboardActivityKind.CheckIn, Guid.NewGuid(), user.Id, 2, true, Date);
        await store.ApplyAsync(grant with { Version = 3, IsEligible = false }, Ct);
        await store.ApplyAsync(grant, Ct);
        Assert.Empty((await store.GetAsync(Week, 1, 20, null, Ct)).Items);
        await store.ApplyAsync(grant with { Version = 4, CreditedAtUtc = Date.AddMonths(1) }, Ct);
        await store.ApplyAsync(grant, Ct);
        Assert.Equal(2, Assert.Single((await store.GetAsync(Week, 1, 20, null, Ct)).Items).Points);
        Assert.Single(await db.ActivityScores.ToArrayAsync(Ct));
        var nextMonth = LeaderboardPeriod.Create("month", new(2026, 11, 1), Date);
        Assert.Empty((await store.GetAsync(nextMonth, 1, 20, null, Ct)).Items);
    }

    [Fact]
    public async Task BoundariesAndMutualBlocksAreRespected()
    {
        await using var db = Db(); var store = Scores(db); var viewer = NewUser("Viewer"); var author = NewUser("Author");
        db.AddRange(viewer, author); await db.SaveChangesAsync(Ct);
        foreach (var date in new[] { Week.StartsAtUtc.AddTicks(-1), Week.StartsAtUtc, Week.EndsAtUtc })
            await store.ApplyAsync(new(LeaderboardActivityKind.CheckIn, Guid.NewGuid(), author.Id, 1, true, date), Ct);
        Assert.Equal(2, Assert.Single((await store.GetAsync(Week, 1, 20, viewer.Id, Ct)).Items).Points);
        await UserBlockStoreTests.Store(db).SetAsync(author.Id, viewer.PublicAuthorId, true, Ct);
        Assert.Empty((await store.GetAsync(Week, 1, 20, viewer.Id, Ct)).Items);
        Assert.Single((await store.GetAsync(Week, 1, 20, null, Ct)).Items);
    }
}
