using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Account.Persistence.Profiles;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Options;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Account.Infrastructure.Tests.Persistence;

public sealed class UserBlockStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    internal static UserBlockStore Store(AccountDbContext db) => new(db, Microsoft.Extensions.Options.Options.Create(new MediaPublicUrlOptions
        { PublicEndpoint = "https://media.example.com", AvatarBucketName = "avatars" }));

    [Fact]
    public async Task BlockIsIdempotent_RemovesBothFollows_AndUnblockPreservesIndependentReverseBlock()
    {
        await using var db = ProfileSocialStoreTests.Db();
        var a = ProfileSocialStoreTests.NewUser("Viewer"); var b = ProfileSocialStoreTests.NewUser("Author");
        db.AddRange(a, b); await db.SaveChangesAsync(Ct);
        var follows = ProfileSocialStoreTests.Store(db); var blocks = Store(db);
        await follows.SetAsync(a.Id, b.Id, true, Ct); await follows.SetAsync(b.Id, a.Id, true, Ct);
        var first = await blocks.SetAsync(a.Id, b.PublicAuthorId, true, Ct);
        var duplicate = await blocks.SetAsync(a.Id, b.PublicAuthorId, true, Ct);
        duplicate.Should().Be(first);
        db.UserFollows.Should().BeEmpty(); db.UserBlocks.Should().ContainSingle();
        (await blocks.ExcludedUserIdsAsync(a.Id, Ct)).Should().Equal(b.Id);
        (await blocks.ExcludedUserIdsAsync(b.Id, Ct)).Should().Equal(a.Id);
        (await blocks.ListAsync(b.Id, 20, null, Ct)).Items.Should().BeEmpty();
        await Assert.ThrowsAsync<NotFoundException>(() => follows.SetAsync(b.Id, a.Id, true, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => follows.SetAsync(a.Id, b.Id, true, Ct));
        await blocks.SetAsync(b.Id, a.PublicAuthorId, true, Ct);
        await blocks.SetAsync(a.Id, b.PublicAuthorId, false, Ct);
        await blocks.SetAsync(a.Id, b.PublicAuthorId, false, Ct);
        (await blocks.ExcludedUserIdsAsync(a.Id, Ct)).Should().Equal(b.Id);
        db.UserFollows.Should().BeEmpty();
        await blocks.SetAsync(b.Id, a.PublicAuthorId, false, Ct);
        (await blocks.ExcludedUserIdsAsync(a.Id, Ct)).Should().BeEmpty();
        db.UserFollows.Should().BeEmpty();
    }

    [Fact]
    public async Task StableIdentitySurvivesSlugChange_AndDeletedAuthorCanBeUnblocked()
    {
        await using var db = ProfileSocialStoreTests.Db();
        var a = ProfileSocialStoreTests.NewUser("Viewer"); var b = ProfileSocialStoreTests.NewUser("Author");
        db.AddRange(a, b); await db.SaveChangesAsync(Ct);
        var blocks = Store(db); var id = b.PublicAuthorId;
        await blocks.SetAsync(a.Id, id, true, Ct);
        b.ClearPublicSlug(); await db.SaveChangesAsync(Ct);
        (await blocks.ListAsync(a.Id, 20, null, Ct)).Items.Single().AuthorId.Should().Be(id);
        id.Should().NotBe(b.Id);
        b.SetSoftDelete(); await db.SaveChangesAsync(Ct);
        var deleted = (await blocks.ListAsync(a.Id, 20, null, Ct)).Items.Single();
        deleted.UserName.Should().Be("Недоступный автор"); deleted.Address.Should().BeNull();
        (await blocks.SetAsync(a.Id, id, false, Ct)).IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task ListIsViewerScoped_PaginationRejectsChangedRevision_AndSelfBlockIsRejected()
    {
        await using var db = ProfileSocialStoreTests.Db();
        var a = ProfileSocialStoreTests.NewUser("Viewer"); var b = ProfileSocialStoreTests.NewUser("Author");
        var c = ProfileSocialStoreTests.NewUser("Third"); db.AddRange(a, b, c); await db.SaveChangesAsync(Ct);
        var blocks = Store(db);
        await Assert.ThrowsAsync<ValidationException>(() => blocks.SetAsync(a.Id, a.PublicAuthorId, true, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => blocks.SetAsync(a.Id, b.Id, true, Ct));
        await blocks.SetAsync(a.Id, b.PublicAuthorId, true, Ct); await blocks.SetAsync(a.Id, c.PublicAuthorId, true, Ct);
        var first = await blocks.ListAsync(a.Id, 1, null, Ct);
        first.NextCursor.Should().NotBeNull();
        var second = await blocks.ListAsync(a.Id, 1, first.NextCursor, Ct);
        second.Revision.Should().Be(first.Revision); second.NextCursor.Should().BeNull();
        second.Items.Single().AuthorId.Should().NotBe(first.Items.Single().AuthorId);
        (await blocks.ListAsync(c.Id, 20, null, Ct)).Items.Should().BeEmpty();
        await blocks.SetAsync(a.Id, b.PublicAuthorId, false, Ct);
        await Assert.ThrowsAsync<ValidationException>(() => blocks.ListAsync(a.Id, 1, first.NextCursor, Ct));
    }

    [Fact]
    public async Task OutgoingLookupIsTargeted_Directional_AndPreservesListPresentation()
    {
        await using var db = ProfileSocialStoreTests.Db();
        var a = ProfileSocialStoreTests.NewUser("Viewer"); var b = ProfileSocialStoreTests.NewUser("Author");
        var c = ProfileSocialStoreTests.NewUser("Other"); db.AddRange(a, b, c); await db.SaveChangesAsync(Ct);
        var blocks = Store(db);
        await blocks.SetAsync(a.Id, b.PublicAuthorId, true, Ct);
        await blocks.SetAsync(a.Id, c.PublicAuthorId, true, Ct);
        var expected = (await blocks.ListAsync(a.Id, 20, null, Ct)).Items.Single(x => x.AuthorId == b.PublicAuthorId);
        (await blocks.FindOutgoingAsync(a.Id, b.Id, Ct)).Should().Be(expected);
        (await blocks.FindOutgoingAsync(b.Id, a.Id, Ct)).Should().BeNull();
        (await blocks.FindOutgoingAsync(a.Id, Guid.NewGuid(), Ct)).Should().BeNull();
        b.SetSoftDelete(); await db.SaveChangesAsync(Ct);
        var deleted = await blocks.FindOutgoingAsync(a.Id, b.Id, Ct);
        deleted.Should().NotBeNull(); deleted!.UserName.Should().Be("Недоступный автор");
        deleted.Address.Should().BeNull(); deleted.AvatarUrl.Should().BeNull();
    }

    [Fact]
    public async Task ConnectionsFilterBeforePaging_AndProfileIsUnavailableInBothDirections()
    {
        await using var db = ProfileSocialStoreTests.Db();
        var a = ProfileSocialStoreTests.NewUser("Viewer"); var b = ProfileSocialStoreTests.NewUser("Blocked");
        var c = ProfileSocialStoreTests.NewUser("Owner"); var d = ProfileSocialStoreTests.NewUser("Visible");
        db.AddRange(a, b, c, d); await db.SaveChangesAsync(Ct);
        var follows = ProfileSocialStoreTests.Store(db);
        await follows.SetAsync(d.Id, c.Id, true, Ct); await follows.SetAsync(b.Id, c.Id, true, Ct);
        await Store(db).SetAsync(a.Id, b.PublicAuthorId, true, Ct);
        var page = await follows.ListAsync(c.Id, false, 1, 1, Ct, a.Id);
        page.TotalCount.Should().Be(1); page.Items.Single().UserName.Should().Be("Visible");
        await Assert.ThrowsAsync<NotFoundException>(() => follows.GetStateAsync(b.Id, a.Id, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => follows.GetStateAsync(a.Id, b.Id, Ct));
    }
}
