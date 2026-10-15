using CoffeePeek.Account.Application.Common.Options;
using CoffeePeek.Account.Domain.Entities;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Account.Persistence.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Options;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Infrastructure.Tests.Persistence;

public sealed class PublicProfileSummaryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetProfiles_ReturnsCurrentAvatarAndAddressInDistinctInputOrder()
    {
        await using var db = Db();
        var withAvatar = NewUser("WithAvatar");
        withAvatar.UpdateAvatar(PhotoMetadata.Create("avatar.jpg", "image/jpeg", "/users/avatar.jpg", 100));
        var withoutAvatar = NewUser("WithoutAvatar");
        db.AddRange(withAvatar, withoutAvatar);
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
        var service = Service(db);

        var profiles = await service.GetProfilesByIdsAsync([withoutAvatar.Id, withAvatar.Id, withoutAvatar.Id], Ct);

        profiles.Select(p => p.Address.EntityId).Should().Equal(withoutAvatar.Id, withAvatar.Id);
        profiles[0].AvatarUrl.Should().BeNull();
        profiles[1].AvatarUrl.Should().Be("https://media.example.com/avatars/users/avatar.jpg");
        profiles[1].Address.Slug.Should().Be(withAvatar.PublicSlug);
        profiles[1].Address.CanonicalPath.Should().Be("/users/" + withAvatar.PublicSlug);
        profiles[1].Address.Revision.Should().Be(withAvatar.SlugRevision);
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task GetProfiles_ReadsReplacementAvatarOnNextRequest()
    {
        await using var db = Db();
        var user = NewUser("FreshAvatar");
        user.UpdateAvatar(PhotoMetadata.Create("first.jpg", "image/jpeg", "users/first.jpg", 100));
        db.Add(user);
        await db.SaveChangesAsync(Ct);
        var service = Service(db);
        (await service.GetProfilesByIdsAsync([user.Id], Ct)).Single().AvatarUrl
            .Should().Be("https://media.example.com/avatars/users/first.jpg");

        user.UpdateAvatar(PhotoMetadata.Create("second.jpg", "image/jpeg", "users/second.jpg", 100));
        db.Add(user.PhotoMetadata!);
        await db.SaveChangesAsync(Ct);

        (await service.GetProfilesByIdsAsync([user.Id], Ct)).Single().AvatarUrl
            .Should().Be("https://media.example.com/avatars/users/second.jpg");
    }

    [Fact]
    public async Task GetProfiles_OmitsBlockedDeletedMissingAndUnaddressedUsers()
    {
        await using var db = Db();
        var visible = NewUser("VisibleUser");
        var blocked = NewUser("BlockedUser");
        blocked.SetBlocked(true);
        var deleted = NewUser("DeletedUser");
        deleted.SetSoftDelete();
        var unaddressed = NewUser("UnaddressedUser");
        db.AddRange(visible, blocked, deleted, unaddressed);
        await db.SaveChangesAsync(Ct);
        unaddressed.ClearPublicSlug();
        await db.SaveChangesAsync(Ct);

        var profiles = await Service(db).GetProfilesByIdsAsync(
            [blocked.Id, Guid.NewGuid(), visible.Id, deleted.Id, unaddressed.Id], Ct);

        profiles.Should().ContainSingle().Which.Address.EntityId.Should().Be(visible.Id);
    }

    [Fact]
    public async Task GetProfiles_RejectsInvalidBatches()
    {
        await using var db = Db();
        var service = Service(db);
        Guid[][] invalidBatches = [[], [Guid.Empty], Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray()];

        foreach (var ids in invalidBatches)
        {
            var read = () => service.GetProfilesByIdsAsync(ids, Ct);
            await read.Should().ThrowAsync<ValidationException>();
        }
    }

    [Fact]
    public async Task GetProfiles_RespectsDisabledPublicAddresses()
    {
        await using var db = new AccountDbContext(new DbContextOptionsBuilder<AccountDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var read = () => Service(db).GetProfilesByIdsAsync([Guid.NewGuid()], Ct);

        await read.Should().ThrowAsync<ServiceUnavailableException>();
    }

    private static AccountDbContext Db() => new(
        new DbContextOptionsBuilder<AccountDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        Microsoft.Extensions.Options.Options.Create(new PublicAddressOptions { Enabled = true }));

    private static ProfileAddressService Service(AccountDbContext db) => new(db,
        Microsoft.Extensions.Options.Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com/", AvatarBucketName = "/avatars/" }));

    private static User NewUser(string username) =>
        User.Register(Guid.NewGuid() + "@example.com", username, "hash", Role.Create("User"));
}
