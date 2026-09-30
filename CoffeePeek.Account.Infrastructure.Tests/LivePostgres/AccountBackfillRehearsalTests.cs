using CoffeePeek.Account.Persistence.PublicAddresses;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class AccountBackfillRehearsalTests : IClassFixture<AccountBackfillRehearsalDatabase>
{
    private readonly AccountBackfillRehearsalDatabase fixture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public AccountBackfillRehearsalTests(AccountBackfillRehearsalDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task LegacyDatabase_MigratesIncrementallyWithoutLosingUsers()
    {
        fixture.BeforeMigrations.Last().Should().Be("20260921104154_AddAccountDeletionConfirmationSecurity");
        fixture.BeforeMigrations.Should().NotContain("20260928134755_AddUserPublicAddresses");
        fixture.RegistryExistedBefore.Should().BeFalse();
        await using var db = fixture.Context(false);
        (await db.Database.GetPendingMigrationsAsync(Ct)).Should().BeEmpty();
        (await db.Database.GetAppliedMigrationsAsync(Ct)).Should().Equal(db.Database.GetMigrations());
        db.Database.HasPendingModelChanges().Should().BeFalse();
        var users = await db.Users.AsNoTracking().Where(x => fixture.OriginalUsernames.Keys.Contains(x.Id)).ToArrayAsync(Ct);
        users.ToDictionary(x => x.Id, x => x.Username.Value).Should().BeEquivalentTo(fixture.OriginalUsernames);
        users.Should().OnlyContain(x => x.UsernameChangedAtUtc == null);
        users.Single(x => x.Id == fixture.BlockedId).IsBlocked.Should().BeTrue();
        users.Single(x => x.Id == fixture.DeletedId).IsSoftDelete.Should().BeTrue();
    }

    [Fact]
    public async Task UserBackfill_ReviewsPagesReplaysFiltersVisibilityHandlesConflictAndRehearsesRollback()
    {
        await using var db = fixture.Context();
        var service = new ProfileAddressService(db);
        (await db.UserPublicAddresses.CountAsync(Ct)).Should().Be(0);
        var previewOnly = new List<PublicAddressBackfillItem>();
        Guid? cursor = null;
        do
        {
            var page = await service.BackfillAsync(cursor, 1, true, Ct);
            page.Items.Should().ContainSingle();
            previewOnly.AddRange(page.Items);
            previewOnly.Count.Should().BeLessThanOrEqualTo(5);
            cursor = page.NextCursor;
        } while (cursor is not null);
        previewOnly.Select(x => x.EntityId).Should().BeEquivalentTo(fixture.OriginalUsernames.Keys.Where(x => x != fixture.DeletedId));
        previewOnly.Where(x => fixture.IvanIds.Contains(x.EntityId)).Should().OnlyContain(x => x.Slug == "ivan" && !x.HadCollision);
        previewOnly.Single(x => x.EntityId == fixture.AdminId).Slug.Should().Be(CoffeePeek.Shared.Domain.PublicAddresses.PublicSlug.Candidate("user", fixture.AdminId, 1));
        (await db.UserPublicAddresses.CountAsync(Ct)).Should().Be(0);
        // Dry-run pages do not reserve earlier proposals: review/write N before reviewing N+1.
        var written = new List<PublicAddressBackfillItem>();
        cursor = null;
        do
        {
            var count = await db.UserPublicAddresses.CountAsync(Ct);
            var preview = await service.BackfillAsync(cursor, 2, true, Ct);
            (await db.UserPublicAddresses.CountAsync(Ct)).Should().Be(count);
            var page = await service.BackfillAsync(cursor, 2, false, Ct);
            page.Items.Length.Should().BeInRange(1, 2);
            page.Items.Should().BeEquivalentTo(preview.Items, options => options.WithStrictOrdering());
            page.NextCursor.Should().Be(preview.NextCursor);
            page.Items.Should().OnlyContain(x => !x.AlreadyInitialized);
            written.AddRange(page.Items);
            written.Count.Should().BeLessThanOrEqualTo(5);
            cursor = page.NextCursor;
        } while (cursor is not null);
        written.Select(x => x.EntityId).Should().OnlyHaveUniqueItems().And.BeEquivalentTo(previewOnly.Select(x => x.EntityId));
        var firstIvan = await db.Users.Where(x => fixture.IvanIds.Contains(x.Id)).OrderBy(x => x.Id).Select(x => x.Id).FirstAsync(Ct);
        written.Single(x => x.EntityId == firstIvan).Slug.Should().Be("ivan");
        foreach (var id in fixture.IvanIds.Where(x => x != firstIvan))
        {
            var item = written.Single(x => x.EntityId == id);
            item.Slug.Should().Be(PublicSlug.Candidate("ivan", id, 1));
            item.HadCollision.Should().BeTrue();
        }
        var replay = await service.BackfillAsync(null, 100, false, Ct);
        replay.Items.Should().OnlyContain(x => x.AlreadyInitialized);
        replay.Items.Select(x => (x.EntityId, x.Slug, x.HadCollision)).Should().Equal(written.Select(x => (x.EntityId, x.Slug, x.HadCollision)));
        (await db.UserPublicAddresses.CountAsync(Ct)).Should().Be(5);
        (await service.ResolveAsync("ivan", Ct))!.EntityId.Should().Be(firstIvan);
        var blockedSlug = written.Single(x => x.EntityId == fixture.BlockedId).Slug;
        (await service.ResolveAsync(blockedSlug, Ct)).Should().BeNull();
        (await service.GetByIdsAsync([fixture.BlockedId, firstIvan], Ct)).Should().ContainSingle(x => x.EntityId == firstIvan);
        var olga = await fixture.RegisterAsync("Olga.Eight", false, Ct);
        var nina = await fixture.RegisterAsync("Nina.Eight", false, Ct);
        var previewConflict = await service.BackfillAsync(null, 100, true, Ct);
        previewConflict.Items.Count(x => !x.AlreadyInitialized).Should().Be(2);
        var conflictSlug = previewConflict.Items.Single(x => x.EntityId == olga).Slug;
        (await db.UserPublicAddresses.CountAsync(x => x.OwnerId == olga || x.OwnerId == nina, Ct)).Should().Be(0);
        await using var blocker = await LivePostgresServer.BeginUncommittedTombstoneAsync(fixture.ConnectionString, "UserPublicAddresses", conflictSlug, Ct);
        var waiting = service.BackfillAsync(null, 100, false, Ct);
        try
        {
            await LivePostgresServer.WaitForLockWaitsAsync(fixture.ConnectionString, "transactionid", 1, Ct);
            waiting.IsCompleted.Should().BeFalse();
            await blocker.CommitAsync(Ct);
            var error = await Assert.ThrowsAsync<ConflictException>(() => waiting);
            error.Message.Should().Be("Address is reserved or changed concurrently.");
            LivePostgresServer.FindPostgres(error)!.SqlState.Should().Be("23505");
            LivePostgresServer.FindPostgres(error)!.ConstraintName.Should().Be("IX_UserPublicAddresses_Slug");
        }
        finally
        {
            await blocker.DisposeAsync();
            try { await waiting; } catch { }
        }
        (await db.UserPublicAddresses.CountAsync(x => x.OwnerId == olga || x.OwnerId == nina, Ct)).Should().Be(0);
        var retryPreview = await service.BackfillAsync(null, 100, true, Ct);
        retryPreview.Items.Single(x => x.EntityId == olga).Slug.Should().Be(PublicSlug.Candidate("Olga.Eight", olga, 1));
        var retry = await service.BackfillAsync(null, 100, false, Ct);
        retry.Items.Should().BeEquivalentTo(retryPreview.Items, options => options.WithStrictOrdering());
        var ninaSlug = retry.Items.Single(x => x.EntityId == nina).Slug;
        await using var disabled = fixture.Context(false);
        var off = new ProfileAddressService(disabled);
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => off.ResolveAsync("ivan", Ct));
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => off.GetByIdsAsync([firstIvan], Ct));
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => off.BackfillAsync(null, 100, true, Ct));
        var deleted = await disabled.Users.SingleAsync(x => x.Id == nina, Ct);
        deleted.SetSoftDelete();
        deleted.RevokeAllSessions();
        await disabled.SaveChangesAsync(Ct);
        var pavel = await fixture.RegisterAsync("Pavel.Nine", false, Ct);
        (await service.ResolveAsync(ninaSlug, Ct)).Should().BeNull();
        (await service.ResolveAsync("ivan", Ct)).Should().NotBeNull();
        (await service.ResolveAsync(blockedSlug, Ct)).Should().BeNull();
        var tombstone = await db.UserPublicAddresses.AsNoTracking().SingleAsync(x => x.Slug == ninaSlug, Ct);
        tombstone.State.Should().Be(PublicAddressState.Tombstone);
        tombstone.OwnerId.Should().BeNull();
        tombstone.Revision.Should().Be(2);
        var reenabled = await service.BackfillAsync(null, 100, false, Ct);
        reenabled.Items.Should().ContainSingle(x => x.EntityId == pavel && !x.AlreadyInitialized);
        reenabled.Items.Should().NotContain(x => x.EntityId == nina || x.EntityId == fixture.DeletedId);
        // Rollback keeps the migration and reservations; only the flag changes.
    }
}
