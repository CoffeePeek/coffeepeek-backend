using System.Text.Json;
using CoffeePeek.Account.Application.Common.Options;
using CoffeePeek.Account.Domain.Entities;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Account.Persistence.Profiles;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Options;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Infrastructure.Tests.Persistence;

public sealed class ProfileSocialStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FollowAndUnfollow_AreIdempotentAndDirectional()
    {
        await using var db = Db();
        var viewer = NewUser("Viewer");
        var owner = NewUser("Owner");
        db.AddRange(viewer, owner);
        await db.SaveChangesAsync(Ct);
        var store = Store(db);

        await store.SetAsync(viewer.Id, owner.Id, true, Ct);
        await store.SetAsync(viewer.Id, owner.Id, true, Ct);
        var state = await store.GetStateAsync(owner.Id, viewer.Id, Ct);
        state.FollowersCount.Should().Be(1);
        state.IsFollowing.Should().BeTrue();
        (await store.GetStateAsync(viewer.Id, owner.Id, Ct)).FollowersCount.Should().Be(0);
        (await store.ListAsync(viewer.Id, true, 1, 20, Ct)).Items.Single().UserName.Should().Be("Owner");

        await store.SetAsync(viewer.Id, owner.Id, false, Ct);
        await store.SetAsync(viewer.Id, owner.Id, false, Ct);
        (await store.GetStateAsync(owner.Id, viewer.Id, Ct)).Should().Be(new CoffeePeek.Contract.Dtos.Profiles.ProfileFollowStateDto(0, false));
    }

    [Fact]
    public async Task Follow_RejectsSelfAndHiddenAccounts()
    {
        await using var db = Db();
        var viewer = NewUser("Viewer");
        var blocked = NewUser("Blocked");
        blocked.SetBlocked(true);
        var deleted = NewUser("Deleted");
        deleted.SetSoftDelete();
        db.AddRange(viewer, blocked, deleted);
        await db.SaveChangesAsync(Ct);
        var store = Store(db);

        await Assert.ThrowsAsync<ValidationException>(() => store.SetAsync(viewer.Id, viewer.Id, true, Ct));
        foreach (var id in new[] { blocked.Id, deleted.Id, Guid.NewGuid() })
        {
            await Assert.ThrowsAsync<NotFoundException>(() => store.SetAsync(viewer.Id, id, true, Ct));
            await Assert.ThrowsAsync<NotFoundException>(() => store.ListAsync(id, false, 1, 20, Ct));
            await Assert.ThrowsAsync<NotFoundException>(() => store.GetStateAsync(id, viewer.Id, Ct));
        }
        db.UserFollows.Should().BeEmpty();
    }

    [Fact]
    public async Task ListsAndCounts_OmitHiddenUsersAndNeverExposeContactsOrInternalIds()
    {
        await using var db = Db();
        var owner = NewUser("Owner");
        var visible = NewUser("Visible");
        visible.UpdateAvatar(PhotoMetadata.Create("avatar.jpg", "image/jpeg", "users/avatar.jpg", 100));
        var blocked = NewUser("Blocked");
        var deleted = NewUser("Deleted");
        var unaddressed = NewUser("NoAddress");
        db.AddRange(owner, visible, blocked, deleted, unaddressed);
        await db.SaveChangesAsync(Ct);
        var store = Store(db);
        foreach (var follower in new[] { visible, blocked, deleted, unaddressed })
            await store.SetAsync(follower.Id, owner.Id, true, Ct);
        blocked.SetBlocked(true);
        deleted.SetSoftDelete();
        unaddressed.ClearPublicSlug();
        await db.SaveChangesAsync(Ct);

        var result = await store.ListAsync(owner.Id, false, 1, 10, Ct);

        result.TotalCount.Should().Be(1);
        result.Items.Single().UserName.Should().Be("Visible");
        result.Items.Single().AvatarUrl.Should().Be("https://media.example.com/avatars/users/avatar.jpg");
        result.Items.Single().Address.CanonicalPath.Should().Be("/users/" + visible.PublicSlug);
        var json = JsonSerializer.Serialize(result);
        json.Should().NotContain("Email").And.NotContain("Phone").And.NotContain(visible.Id.ToString());
        (await store.GetStateAsync(owner.Id, visible.Id, Ct)).FollowersCount.Should().Be(1);
        (await store.GetStateAsync(owner.Id, blocked.Id, Ct)).IsFollowing.Should().BeFalse();
        (await store.GetStateAsync(owner.Id, null, Ct)).IsFollowing.Should().BeFalse();
    }

    [Fact]
    public async Task Lists_ArePagedAndValidateBounds()
    {
        await using var db = Db();
        var owner = NewUser("Owner");
        var first = NewUser("First");
        var second = NewUser("Second");
        db.AddRange(owner, first, second);
        await db.SaveChangesAsync(Ct);
        var store = Store(db);
        await store.SetAsync(first.Id, owner.Id, true, Ct);
        await store.SetAsync(second.Id, owner.Id, true, Ct);

        var pageOne = await store.ListAsync(owner.Id, false, 1, 1, Ct);
        var pageTwo = await store.ListAsync(owner.Id, false, 2, 1, Ct);
        pageOne.TotalCount.Should().Be(2);
        pageOne.Items.Should().HaveCount(1);
        pageTwo.Items.Should().HaveCount(1);
        pageOne.Items[0].Address.Should().NotBe(pageTwo.Items[0].Address);
        foreach (var (page, size) in new[] { (0, 20), (10001, 20), (1, 0), (1, 101) })
            await Assert.ThrowsAsync<ValidationException>(() => store.ListAsync(owner.Id, false, page, size, Ct));
    }

    internal static AccountDbContext Db() => new(
        new DbContextOptionsBuilder<AccountDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        Microsoft.Extensions.Options.Options.Create(new PublicAddressOptions { Enabled = true }));

    internal static ProfileSocialStore Store(AccountDbContext db) => new(db,
        Microsoft.Extensions.Options.Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com", AvatarBucketName = "avatars" }));

    internal static User NewUser(string name) => User.Register(Guid.NewGuid() + "@example.com", name, "hash", Role.Create("User"));
}
