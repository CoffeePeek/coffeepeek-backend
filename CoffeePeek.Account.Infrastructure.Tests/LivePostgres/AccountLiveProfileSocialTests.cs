using CoffeePeek.Account.Persistence.Profiles;
using CoffeePeek.Contract.Dtos.Profiles;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class AccountLiveProfileSocialTests : IClassFixture<AccountLiveDatabase>
{
    private readonly AccountLiveDatabase fixture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public AccountLiveProfileSocialTests(AccountLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentFollowAndUnfollow_KeepOneLinkAndQueryVisibleLists()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var author = await fixture.RegisterAsync("SocialOwner" + suffix, true, Ct);
        var viewer = await fixture.RegisterAsync("SocialViewer" + suffix, true, Ct);
        async Task Set(bool following)
        {
            await using var db = fixture.Context();
            await Store(db).SetAsync(viewer, author, following, Ct);
        }

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Set(true)));
        await using (var db = fixture.Context())
        {
            var store = Store(db);
            (await store.GetStateAsync(author, viewer, Ct)).Should().Be(new ProfileFollowStateDto(1, true));
            (await store.ListAsync(author, false, 1, 20, Ct)).Items.Single().UserName.Should().Be("SocialViewer" + suffix);
            (await store.ListAsync(viewer, true, 1, 20, Ct)).Items.Single().UserName.Should().Be("SocialOwner" + suffix);
            var follower = await db.Users.SingleAsync(u => u.Id == viewer, Ct);
            follower.SetBlocked(true);
            await db.SaveChangesAsync(Ct);
            (await store.GetStateAsync(author, viewer, Ct)).Should().Be(new ProfileFollowStateDto(0, false));
            (await store.ListAsync(author, false, 1, 20, Ct)).Items.Should().BeEmpty();
            follower.SetBlocked(false);
            await db.SaveChangesAsync(Ct);
        }
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Set(false)));
        await using var check = fixture.Context();
        (await check.UserFollows.CountAsync(f => f.FollowerId == viewer && f.FollowedId == author, Ct)).Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentApprovals_DeduplicateRoastersAndDoNotLoseDistinctContributions()
    {
        var author = await fixture.RegisterAsync("Credits" + Guid.NewGuid().ToString("N")[..8], true, Ct);
        var replayed = Guid.NewGuid();
        var roasters = Enumerable.Repeat(replayed, 5).Concat(Enumerable.Range(0, 5).Select(_ => Guid.NewGuid())).ToArray();
        async Task Credit(Guid roaster)
        {
            await using var db = fixture.Context();
            await new RoasterContributionStore(db).CreditAsync(author, roaster, Ct);
        }

        await Task.WhenAll(roasters.Select(Credit));

        await using var check = fixture.Context();
        (await check.UserRoasterCredits.CountAsync(c => c.UserId == author, Ct)).Should().Be(6);
        (await check.Users.SingleAsync(u => u.Id == author, Ct)).Statistics.AddedRoastersCount.Should().Be(6);
        await new RoasterContributionStore(check).CreditAsync(Guid.NewGuid(), Guid.NewGuid(), Ct);
    }

    private static ProfileSocialStore Store(CoffeePeek.Account.Persistence.Configuration.AccountDbContext db) => new(db,
        Microsoft.Extensions.Options.Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com", AvatarBucketName = "avatars" }));
}
