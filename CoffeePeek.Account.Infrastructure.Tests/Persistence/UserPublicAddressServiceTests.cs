using CoffeePeek.Account.Application.Common.Options;
using CoffeePeek.Account.Domain.Entities.PublicAddressAggregate;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Account.Persistence.PublicAddresses;
using CoffeePeek.Account.Persistence.Repositories;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Persistence.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace CoffeePeek.Account.Infrastructure.Tests.Persistence;

/// <summary>
/// Public lookup and admin backfill of user profile addresses (D-03, D-08, D-10). Writes go through the real
/// <see cref="UserRepository"/>, <see cref="UnitOfWork{TDbContext}"/> and the SaveChanges hook on a fresh context per
/// request; every service call runs on its own fresh enabled context. InMemory proves behavior only: uniqueness,
/// the advisory lock and the 409 mapping of a concurrent allocation are live PostgreSQL gate items.
/// </summary>
public sealed class UserPublicAddressServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InMemoryDatabaseRoot _root = new();
    private readonly string _name = Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Resolve_CanonicalAliasCaseInsensitive_AndUnconfirmedEmailIsVisible()
    {
        var id = await RegisterAsync(true, "Ivan.Petrov");
        await using (var check = Db(false))
            (await check.Users.AsNoTracking().SingleAsync(u => u.Id == id, Ct)).Credentials.EmailConfirmed
                .Should().BeFalse("visibility must not depend on email confirmation");

        var canonical = await ResolveAsync("IVAN-PETROV");
        canonical.Should().Be(new PublicAddressMetadata(id, "ivan-petrov", "/users/ivan-petrov", 1, false));

        await RenameAsync(true, id, "Petr");

        var alias = await ResolveAsync("ivan-petrov");
        alias.Should().Be(new PublicAddressMetadata(id, "petr", "/users/petr", 2, true));
        var current = await ResolveAsync("petr");
        current.Should().Be(new PublicAddressMetadata(id, "petr", "/users/petr", 2, false));

        (await ResolveAsync("unknown-person")).Should().BeNull();
    }

    [Fact]
    public async Task Blocked_IsHiddenEverywhere_AndUnblockRestoresSameAddress()
    {
        var id = await RegisterAsync(true, "Ivan.Petrov");
        (await ResolveAsync("ivan-petrov")).Should().NotBeNull();
        (await GetByIdsAsync([id])).Should().ContainSingle();

        await MutateAsync(true, id, u => u.SetBlocked(true), callUpdate: true);

        (await ResolveAsync("ivan-petrov")).Should().BeNull();
        (await GetByIdsAsync([id])).Should().BeEmpty();

        await MutateAsync(true, id, u => u.SetBlocked(false), callUpdate: true);

        var restored = await ResolveAsync("ivan-petrov");
        restored.Should().NotBeNull();
        restored!.Slug.Should().Be("ivan-petrov");
        restored.Revision.Should().Be(1);
        (await RowsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task SoftDeleted_NeverResolves_IncludingLeftoverCanonicalRow()
    {
        // Part A: the lifecycle hook tombstones every row on soft delete.
        var id = await RegisterAsync(true, "Ivan.Petrov");
        await RenameAsync(true, id, "Petr");
        (await ResolveAsync("petr")).Should().NotBeNull();
        (await ResolveAsync("ivan-petrov")).Should().NotBeNull();

        await SoftDeleteAsync(true, id);

        (await ResolveAsync("petr")).Should().BeNull();
        (await ResolveAsync("ivan-petrov")).Should().BeNull();
        (await GetByIdsAsync([id])).Should().BeEmpty();

        // Part B (legacy defense): a canonical row that still points at a soft-deleted owner is never public.
        var ghost = await SeedGhostAsync("Ghost.User", "ghost-user");
        (await RowsAsync()).Should().ContainSingle(r => r.OwnerId == ghost && r.State == PublicAddressState.Canonical,
            "the leftover row must exist, otherwise the visibility check below is vacuous");

        (await ResolveAsync("ghost-user")).Should().BeNull();
        (await GetByIdsAsync([ghost])).Should().BeEmpty();
    }

    [Fact]
    public async Task Batch_ReturnsVisibleCanonicalInInputOrder_AndValidatesIds()
    {
        var a = await RegisterAsync(true, "Anna");
        var b = await RegisterAsync(true, "Boris");
        var c = await RegisterAsync(true, "Clara");
        await MutateAsync(true, c, u => u.SetBlocked(true), callUpdate: true);
        (await RowsAsync()).Should().ContainSingle(r => r.OwnerId == c && r.State == PublicAddressState.Canonical,
            "a blocked user keeps its canonical row");
        var d = await SeedGhostAsync("Ghost.User", "ghost-user");
        var unknown = Guid.NewGuid();

        var result = await GetByIdsAsync([b, unknown, c, a, d, b]);

        result.Select(m => m.EntityId).Should().Equal(b, a);
        result.Should().OnlyContain(m => !m.IsAlias && m.CanonicalPath == "/users/" + m.Slug);
        result.Select(m => m.Slug).Should().Equal("boris", "anna");

        var empty = () => GetByIdsAsync([]);
        await empty.Should().ThrowAsync<ValidationException>();
        var tooMany = () => GetByIdsAsync(Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray());
        await tooMany.Should().ThrowAsync<ValidationException>();
        var emptyId = () => GetByIdsAsync([Guid.Empty]);
        await emptyId.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Resolve_InvalidSlug_Throws400()
    {
        // The last two values are not compile-time constants, so this is a loop rather than InlineData.
        foreach (var slug in new[] { "admin", "Not A Slug", "ivan--petrov", Guid.NewGuid().ToString(), new string('a', 101) })
        {
            var resolve = () => ResolveAsync(slug);
            await resolve.Should().ThrowAsync<ValidationException>($"'{slug}' is not a valid public address");
        }
    }

    [Fact]
    public async Task Disabled_EveryMethodThrows503()
    {
        await using var db = Db(false);
        var service = new ProfileAddressService(db);

        var resolve = () => service.ResolveAsync("ivan", Ct);
        await resolve.Should().ThrowAsync<ServiceUnavailableException>();
        var batch = () => service.GetByIdsAsync([Guid.NewGuid()], Ct);
        await batch.Should().ThrowAsync<ServiceUnavailableException>();
        var backfill = () => service.BackfillAsync(null, 10, true, Ct);
        await backfill.Should().ThrowAsync<ServiceUnavailableException>();
    }

    [Fact]
    public async Task Backfill_DryRunCursorReplay_ExcludesDeletedIncludesBlocked()
    {
        // Seed while disabled, in one request: no address rows exist before the backfill.
        var blocked = NewUser("Blocked.User");
        blocked.SetBlocked(true);
        var deleted = NewUser("Deleted.User");
        deleted.SetSoftDelete();
        var ivanUpper = NewUser("Ivan");
        var ivanLower = NewUser("ivan");
        var admin = NewUser("admin");
        await using (var seed = Db(false))
        {
            var repository = new UserRepository(seed);
            foreach (var user in new[] { ivanUpper, ivanLower, admin, blocked, deleted })
                await repository.Add(user, Ct);
            await new UnitOfWork<AccountDbContext>(seed).SaveChangesAsync(Ct);
        }
        (await RowsAsync()).Should().BeEmpty();
        var eligible = new[] { ivanUpper.Id, ivanLower.Id, admin.Id, blocked.Id };

        // Dry-run pages by user id and writes nothing.
        var page1 = await BackfillAsync(null, 2, true);
        page1.DryRun.Should().BeTrue();
        page1.Items.Should().HaveCount(2);
        page1.NextCursor.Should().Be(page1.Items[1].EntityId);
        (await RowsAsync()).Should().BeEmpty();

        var page2 = await BackfillAsync(page1.NextCursor, 100, true);
        page2.Items.Should().HaveCount(2);
        page2.NextCursor.Should().BeNull();
        page1.Items.Concat(page2.Items).Select(i => i.EntityId).Should().BeEquivalentTo(eligible);

        var full = await BackfillAsync(null, 100, true);
        full.Items.Should().HaveCount(4);
        full.NextCursor.Should().BeNull();
        (await RowsAsync()).Should().BeEmpty();
        full.Items.Select(i => i.EntityId).Should().BeEquivalentTo(eligible);
        full.Items.Should().NotContain(i => i.EntityId == deleted.Id, "soft-deleted users are excluded");
        full.Items.Should().Contain(i => i.EntityId == blocked.Id, "blocked users are allocated but hidden");
        full.Items.Should().ContainSingle(i => i.Slug == "ivan");
        var suffixed = full.Items.Should().ContainSingle(i => i.Slug.StartsWith("ivan-", StringComparison.Ordinal)).Which;
        suffixed.HadCollision.Should().BeTrue();
        suffixed.Slug.Should().Be(PublicSlug.Candidate("ivan", suffixed.EntityId, 1));
        var adminItem = full.Items.Single(i => i.EntityId == admin.Id);
        adminItem.Slug.Should().Be("user-" + admin.Id.ToString("N")[..8]);
        adminItem.Slug.Should().Be(PublicSlug.Candidate("user", admin.Id, 1));
        adminItem.HadCollision.Should().BeFalse();
        full.Items.Single(i => i.EntityId == blocked.Id).Slug.Should().Be("blocked-user");
        full.Items.Should().OnlyContain(i => !i.AlreadyInitialized);
        var planned = full.Items.ToDictionary(i => i.EntityId, i => i.Slug);

        // Write mode allocates exactly what the dry run proposed.
        var written = await BackfillAsync(null, 100, false);
        written.DryRun.Should().BeFalse();
        written.Items.ToDictionary(i => i.EntityId, i => i.Slug).Should().BeEquivalentTo(planned);
        written.Items.Should().OnlyContain(i => !i.AlreadyInitialized);
        var rows = await RowsAsync();
        rows.Should().HaveCount(4);
        rows.Should().OnlyContain(r => r.State == PublicAddressState.Canonical && r.Revision == 1);
        rows.ToDictionary(r => r.OwnerId!.Value, r => r.Slug).Should().BeEquivalentTo(planned);

        // Replay never renames.
        var replay = await BackfillAsync(null, 100, false);
        replay.Items.Should().OnlyContain(i => i.AlreadyInitialized);
        replay.Items.ToDictionary(i => i.EntityId, i => i.Slug).Should().BeEquivalentTo(planned);
        (await RowsAsync()).Should().HaveCount(4);

        (await ResolveAsync(planned[blocked.Id])).Should().BeNull("a blocked user is allocated but hidden");

        var zero = () => BackfillAsync(null, 0, true);
        await zero.Should().ThrowAsync<ValidationException>();
        var over = () => BackfillAsync(null, 101, true);
        await over.Should().ThrowAsync<ValidationException>();
    }

    private async Task<PublicAddressMetadata?> ResolveAsync(string slug)
    {
        await using var db = Db(true);
        return await new ProfileAddressService(db).ResolveAsync(slug, Ct);
    }

    private async Task<PublicAddressMetadata[]> GetByIdsAsync(Guid[] ids)
    {
        await using var db = Db(true);
        return await new ProfileAddressService(db).GetByIdsAsync(ids, Ct);
    }

    private async Task<PublicAddressBackfillResult> BackfillAsync(Guid? afterId, int limit, bool dryRun)
    {
        await using var db = Db(true);
        return await new ProfileAddressService(db).BackfillAsync(afterId, limit, dryRun, Ct);
    }

    // A soft-deleted user with a canonical row that the hook never retired (legacy data written before retirement).
    private async Task<Guid> SeedGhostAsync(string username, string slug)
    {
        var ghost = NewUser(username);
        ghost.SetSoftDelete();
        await using var db = Db(false);
        db.Users.Add(ghost);
        db.UserPublicAddresses.Add(UserPublicAddress.Create(ghost.Id, slug, 1, DateTime.UtcNow));
        await db.SaveChangesAsync(Ct);
        return ghost.Id;
    }

    private AccountDbContext Db(bool enabled) => Db(_root, _name, enabled);

    private static AccountDbContext Db(InMemoryDatabaseRoot root, string name, bool enabled) =>
        new(new DbContextOptionsBuilder<AccountDbContext>().UseInMemoryDatabase(name, root).Options,
            MsOptions.Create(new PublicAddressOptions { Enabled = enabled }));

    // A new Role per user: reusing a persisted Role instance in a new context would re-insert it.
    private static User NewUser(string username) =>
        User.Register($"{Guid.NewGuid():N}@example.com", username, "hash", Role.Create("User"));

    private async Task<Guid> RegisterAsync(bool enabled, string username)
    {
        var user = NewUser(username);
        await using var db = Db(enabled);
        await new UserRepository(db).Add(user, Ct);
        await new UnitOfWork<AccountDbContext>(db).SaveChangesAsync(Ct);
        return user.Id;
    }

    private Task RenameAsync(bool enabled, Guid id, string username) =>
        MutateAsync(enabled, id, u => u.UpdateUsername(Username.Create(username)), callUpdate: true);

    // The account-deletion confirmation and admin-delete path.
    private Task SoftDeleteAsync(bool enabled, Guid id) =>
        MutateAsync(enabled, id, u =>
        {
            u.SetSoftDelete();
            u.RevokeAllSessions();
        }, callUpdate: true);

    private async Task MutateAsync(bool enabled, Guid id, Action<User> change, bool callUpdate)
    {
        await using var db = Db(enabled);
        var repository = new UserRepository(db);
        var user = await repository.GetById(id, Ct) ?? throw new InvalidOperationException($"User {id} not found.");
        change(user);
        if (callUpdate) await repository.Update(user, Ct);
        await new UnitOfWork<AccountDbContext>(db).SaveChangesAsync(Ct);
    }

    private async Task<List<UserPublicAddress>> RowsAsync()
    {
        await using var db = Db(false);
        return await db.UserPublicAddresses.AsNoTracking().ToListAsync(Ct);
    }
}
