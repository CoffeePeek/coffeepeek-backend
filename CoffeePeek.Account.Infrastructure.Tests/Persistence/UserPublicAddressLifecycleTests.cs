using CoffeePeek.Account.Application.Common.Options;
using CoffeePeek.Account.Domain.Entities.PublicAddressAggregate;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Account.Persistence.Repositories;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Persistence.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace CoffeePeek.Account.Infrastructure.Tests.Persistence;

/// <summary>
/// Lifecycle of user profile addresses through the AccountDbContext SaveChanges hook (D-01, D-03).
/// Every request uses a fresh context, the real <see cref="UserRepository"/> and <see cref="UnitOfWork{TDbContext}"/>,
/// exactly like the generated Wolverine handlers. InMemory proves behavior only: uniqueness, FK, checks,
/// savepoint rollback and the advisory lock are live PostgreSQL gate items.
/// </summary>
public sealed class UserPublicAddressLifecycleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InMemoryDatabaseRoot _root = new();
    private readonly string _name = Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Register_AllocatesCanonicalFromUsername_ForEmailAndGoogleUsers()
    {
        var emailUser = await RegisterAsync(true, "Ivan.Petrov");

        var rows = await RowsAsync();
        var row = rows.Should().ContainSingle().Which;
        row.Slug.Should().Be("ivan-petrov");
        row.State.Should().Be(PublicAddressState.Canonical);
        row.Revision.Should().Be(1);
        row.OwnerId.Should().Be(emailUser);

        var google = User.CreateExternal("ivan.petrov1985@gmail.com", "Google", "google-sub-1");
        await AddAsync(true, google);

        rows = await RowsAsync();
        rows.Should().HaveCount(2);
        var googleRow = rows.Should().ContainSingle(r => r.OwnerId == google.Id).Which;
        googleRow.Slug.Should().Be("ivan-petrov1985");
        googleRow.State.Should().Be(PublicAddressState.Canonical);
        googleRow.Revision.Should().Be(1);
    }

    [Fact]
    public async Task Register_CollidingAndUnsupportedUsernames_GetDeterministicSuffixes()
    {
        var ids = new List<Guid>();
        foreach (var username in new[] { "Ivan", "ivan", "Иван", "admin", "日本語", "User" })
            ids.Add(await RegisterAsync(true, username));

        var rows = await RowsAsync();
        var slugs = ids.Select(id => Canonical(rows, id).Slug).ToArray();

        slugs.Should().Equal(
            "ivan",
            PublicSlug.Candidate("ivan", ids[1], 1),
            PublicSlug.Candidate("ivan", ids[2], 1),
            "user-" + ids[3].ToString("N")[..8],
            "user-" + ids[4].ToString("N")[..8],
            "user-" + ids[5].ToString("N")[..8]);
        slugs.Should().OnlyHaveUniqueItems();
        rows.Should().HaveCount(6);
        rows.Should().NotContain(r => r.Slug == "place" || r.Slug == "user");
        rows.Should().OnlyContain(r => r.Revision == 1 && r.State == PublicAddressState.Canonical);
    }

    [Fact]
    public async Task Rename_FollowsUsername_AndRestoresOwnAlias_AtoBtoA()
    {
        var id = await RegisterAsync(true, "Ivan.Petrov");
        var original = (await RowsAsync()).Should().ContainSingle().Which;
        original.Revision.Should().Be(1);

        await RenameAsync(true, id, "Petr");

        var rows = await RowsAsync();
        rows.Should().HaveCount(2);
        var r1 = rows.Single(r => r.Id == original.Id);
        r1.State.Should().Be(PublicAddressState.Alias);
        r1.Revision.Should().Be(2);
        var petr = rows.Single(r => r.Slug == "petr");
        petr.State.Should().Be(PublicAddressState.Canonical);
        petr.Revision.Should().Be(2);

        await RenameAsync(true, id, "Ivan.Petrov");

        rows = await RowsAsync();
        rows.Should().HaveCount(2);
        var restored = rows.Single(r => r.Id == original.Id);
        restored.Slug.Should().Be("ivan-petrov");
        restored.State.Should().Be(PublicAddressState.Canonical);
        restored.Revision.Should().Be(3);
        var demoted = rows.Single(r => r.Slug == "petr");
        demoted.Id.Should().Be(petr.Id);
        demoted.State.Should().Be(PublicAddressState.Alias);
        demoted.Revision.Should().Be(3);
        rows.Should().OnlyContain(r => r.OwnerId == id);
    }

    [Fact]
    public async Task Rename_CaseOrPunctuationOnly_IsNoOp_IncludingOwnSuffix()
    {
        var single = await RegisterAsync(true, "Ivan.Petrov");
        await RenameAsync(true, single, "ivan_petrov");

        var rows = await RowsAsync();
        var row = rows.Should().ContainSingle().Which;
        row.Slug.Should().Be("ivan-petrov");
        row.State.Should().Be(PublicAddressState.Canonical);
        row.Revision.Should().Be(1);
        (await UsernameAsync(single)).Should().Be("ivan_petrov", "the username change itself must still be saved");

        await RegisterAsync(true, "Ivan");
        var b = await RegisterAsync(true, "ivan");
        var suffixed = PublicSlug.Candidate("ivan", b, 1);
        Canonical(await RowsAsync(), b).Slug.Should().Be(suffixed);

        await RenameAsync(true, b, "IVAN");

        rows = await RowsAsync();
        var owned = rows.Where(r => r.OwnerId == b).Should().ContainSingle().Which;
        owned.Slug.Should().Be(suffixed);
        owned.State.Should().Be(PublicAddressState.Canonical);
        owned.Revision.Should().Be(1);
        rows.Should().NotContain(r => r.Slug == PublicSlug.Candidate("ivan", b, 2));
        (await UsernameAsync(b)).Should().Be("IVAN");
    }

    [Fact]
    public async Task Rename_ToAnotherUsersAliasOrTombstone_GetsOwnSuffix()
    {
        var x = await RegisterAsync(true, "Petr");
        await RenameAsync(true, x, "Pavel");
        var xAlias = (await RowsAsync()).Single(r => r.Slug == "petr");
        xAlias.State.Should().Be(PublicAddressState.Alias);
        xAlias.OwnerId.Should().Be(x);

        var y = await RegisterAsync(true, "Yuri");
        await RenameAsync(true, y, "Petr");

        var rows = await RowsAsync();
        var yCanonical = Canonical(rows, y);
        yCanonical.Slug.Should().Be(PublicSlug.Candidate("petr", y, 1));
        yCanonical.Revision.Should().Be(2);
        var yuri = rows.Single(r => r.Slug == "yuri");
        yuri.State.Should().Be(PublicAddressState.Alias);
        yuri.OwnerId.Should().Be(y);
        var petr = rows.Single(r => r.Slug == "petr");
        petr.State.Should().Be(PublicAddressState.Alias);
        petr.OwnerId.Should().Be(x);

        var z = await RegisterAsync(true, "Olga");
        await SoftDeleteAsync(true, z);
        (await RowsAsync()).Single(r => r.Slug == "olga").State.Should().Be(PublicAddressState.Tombstone);

        var w = await RegisterAsync(true, "olga_");

        rows = await RowsAsync();
        Canonical(rows, w).Slug.Should().Be(PublicSlug.Candidate("olga", w, 1));
        var olga = rows.Single(r => r.Slug == "olga");
        olga.State.Should().Be(PublicAddressState.Tombstone);
        olga.OwnerId.Should().BeNull();
    }

    [Fact]
    public async Task UnrelatedUpdates_NeverRenameAllocateOrRetire()
    {
        var id = await RegisterAsync(true, "Ivan.Petrov");
        await AssertUntouched();

        // Disabled rename is ignored: from here on the canonical deliberately differs from the username's
        // slug ("petr"), so an IsModified-based detector would rename on any later Update.
        await RenameAsync(false, id, "Petr");
        (await UsernameAsync(id)).Should().Be("Petr");
        await AssertUntouched();

        await MutateAsync(true, id, u => u.UpdateAbout("Hello"), callUpdate: true);
        await AssertUntouched();

        await MutateAsync(true, id, u => u.Statistics.IncrementReviews(), callUpdate: false);
        await AssertUntouched();

        await MutateAsync(true, id, u => u.SetBlocked(true), callUpdate: true);
        await AssertUntouched();

        await MutateAsync(true, id, u => u.SetBlocked(false), callUpdate: true);
        await AssertUntouched();

        async Task AssertUntouched()
        {
            var row = (await RowsAsync()).Should().ContainSingle().Which;
            row.Slug.Should().Be("ivan-petrov");
            row.State.Should().Be(PublicAddressState.Canonical);
            row.Revision.Should().Be(1);
            row.OwnerId.Should().Be(id);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Deletion_RetiresEveryAddress_EvenWhenDisabled(bool enabledAtDeletion)
    {
        var id = await RegisterAsync(true, "Ivan.Petrov");
        await RenameAsync(true, id, "Petr");
        var before = await RowsAsync();
        before.Should().HaveCount(2);
        before.Should().OnlyContain(r => r.OwnerId == id && r.Revision == 2);

        await SoftDeleteAsync(enabledAtDeletion, id);

        var rows = await RowsAsync();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.State == PublicAddressState.Tombstone && r.OwnerId == null && r.Revision == 3);
        rows.Count(r => r.OwnerId == id).Should().Be(0);
        rows.Select(r => r.Slug).Should().BeEquivalentTo("ivan-petrov", "petr");
    }

    [Fact]
    public async Task HardDelete_RetiresBeforeRemovingUser()
    {
        var id = await RegisterAsync(true, "Ivan.Petrov");
        (await RowsAsync()).Should().ContainSingle(r => r.OwnerId == id && r.State == PublicAddressState.Canonical);

        await using (var db = Db(false))
        {
            db.Users.Remove(await db.Users.SingleAsync(u => u.Id == id, Ct));
            db.UserPublicAddresses.Local.Should().BeEmpty("no address row is loaded before Remove");
            var save = () => new UnitOfWork<AccountDbContext>(db).SaveChangesAsync(Ct);
            await save.Should().NotThrowAsync();
        }

        await using (var check = Db(false))
            (await check.Users.AnyAsync(u => u.Id == id, Ct)).Should().BeFalse();
        var row = (await RowsAsync()).Should().ContainSingle().Which;
        row.Slug.Should().Be("ivan-petrov");
        row.State.Should().Be(PublicAddressState.Tombstone);
        row.OwnerId.Should().BeNull();
        row.Revision.Should().Be(2);
    }

    [Fact]
    public async Task DeletedUser_UsernameRewrite_AllocatesNothing()
    {
        var id = await RegisterAsync(true, "Ivan.Petrov");
        await SoftDeleteAsync(true, id);
        var tombstone = (await RowsAsync()).Should().ContainSingle().Which;
        tombstone.State.Should().Be(PublicAddressState.Tombstone);
        tombstone.Revision.Should().Be(2);

        await RenameAsync(true, id, "Anonymized");

        (await UsernameAsync(id)).Should().Be("Anonymized");
        var rows = await RowsAsync();
        rows.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            tombstone.Id, Slug = "ivan-petrov", State = PublicAddressState.Tombstone, OwnerId = (Guid?)null, Revision = 2L
        });
        rows.Should().NotContain(r => r.OwnerId == id);
        rows.Should().NotContain(r => r.Slug == "anonymized");
    }

    [Fact]
    public async Task Disabled_NoAllocation_ThenFirstEnabledRenameAllocates()
    {
        var id = await RegisterAsync(false, "Ivan.Petrov");
        await RenameAsync(false, id, "Petr");
        (await RowsAsync()).Should().BeEmpty();
        (await UsernameAsync(id)).Should().Be("Petr");

        await RenameAsync(true, id, "Pavel");

        var row = (await RowsAsync()).Should().ContainSingle().Which;
        row.Slug.Should().Be("pavel");
        row.State.Should().Be(PublicAddressState.Canonical);
        row.Revision.Should().Be(1);
        row.OwnerId.Should().Be(id);
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
        await AddAsync(enabled, user);
        return user.Id;
    }

    private async Task AddAsync(bool enabled, User user)
    {
        await using var db = Db(enabled);
        await new UserRepository(db).Add(user, Ct);
        await new UnitOfWork<AccountDbContext>(db).SaveChangesAsync(Ct);
    }

    private Task RenameAsync(bool enabled, Guid id, string username) =>
        MutateAsync(enabled, id, u => u.UpdateUsername(Username.Create(username),
            u.UsernameChangedAtUtc?.AddDays(7) ?? DateTime.UtcNow), callUpdate: true);

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

    private async Task<string> UsernameAsync(Guid id)
    {
        await using var db = Db(false);
        return (await db.Users.AsNoTracking().SingleAsync(u => u.Id == id, Ct)).Username.Value;
    }

    private static UserPublicAddress Canonical(IEnumerable<UserPublicAddress> rows, Guid ownerId) =>
        rows.Should().ContainSingle(r => r.OwnerId == ownerId && r.State == PublicAddressState.Canonical).Which;
}
