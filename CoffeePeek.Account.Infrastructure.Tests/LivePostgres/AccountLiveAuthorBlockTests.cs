using CoffeePeek.Account.Infrastructure.Tests.Persistence;
using CoffeePeek.Account.Persistence.Profiles;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class AccountLiveAuthorBlockTests : IClassFixture<AccountLiveDatabase>
{
    private readonly AccountLiveDatabase fixture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public AccountLiveAuthorBlockTests(AccountLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task MigratedDatabaseSerializesConcurrentBlockAndFollow_AndKeepsUnblockDirectional()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var a = await fixture.RegisterAsync("Blocker" + suffix, true, Ct);
        var b = await fixture.RegisterAsync("Author" + suffix, true, Ct);
        await using var read = fixture.Context();
        var publicA = await read.Users.Where(x => x.Id == a).Select(x => x.PublicAuthorId).SingleAsync(Ct);
        var publicB = await read.Users.Where(x => x.Id == b).Select(x => x.PublicAuthorId).SingleAsync(Ct);
        publicA.Should().NotBe(a).And.NotBe(Guid.Empty); publicB.Should().NotBe(b).And.NotBe(publicA);
        async Task Block(bool reverse)
        {
            await using var db = fixture.Context();
            await UserBlockStoreTests.Store(db).SetAsync(reverse ? b : a, reverse ? publicA : publicB, true, Ct);
        }
        async Task Follow(bool reverse)
        {
            await using var db = fixture.Context();
            try { await ProfileSocialStoreTests.Store(db).SetAsync(reverse ? b : a, reverse ? a : b, true, Ct); }
            catch (NotFoundException) { /* A committed block wins over the racing follow. */ }
        }
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => i % 3 == 0 ? Block(false) : Follow(i % 2 == 0)));
        (await read.UserBlocks.CountAsync(x => x.BlockerId == a && x.BlockedId == b, Ct)).Should().Be(1);
        (await read.UserFollows.CountAsync(x => (x.FollowerId == a && x.FollowedId == b) || (x.FollowerId == b && x.FollowedId == a), Ct)).Should().Be(0);
        await Block(true);
        await UserBlockStoreTests.Store(read).SetAsync(a, publicB, false, Ct);
        (await UserBlockStoreTests.Store(read).ExcludedUserIdsAsync(a, Ct)).Should().Contain(b);
        await UserBlockStoreTests.Store(read).SetAsync(b, publicA, false, Ct);
        (await UserBlockStoreTests.Store(read).ExcludedUserIdsAsync(a, Ct)).Should().NotContain(b);
    }
}
