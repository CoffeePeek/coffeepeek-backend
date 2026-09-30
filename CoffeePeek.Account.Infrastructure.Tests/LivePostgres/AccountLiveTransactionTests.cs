using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Application.Features.User.UpdateUserProfile.UpdateUsername;
using CoffeePeek.Account.Persistence.Repositories;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Account.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class AccountLiveTransactionTests : IClassFixture<AccountLiveDatabase>
{
    private readonly AccountLiveDatabase fixture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    public AccountLiveTransactionTests(AccountLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentCreation_WaitsOnAccountLockAndAllocatesDistinctSlugs()
    {
        await using var first = fixture.Context();
        await using var second = fixture.Context();
        await using var third = fixture.Context();
        await using var transaction = await first.Database.BeginTransactionAsync(Ct);
        var roleA = await first.Roles.SingleAsync(Ct);
        var roleB = await second.Roles.SingleAsync(Ct);
        var a = User.Register("lock-one@example.com", "Иван", "hash", roleA);
        var b = User.Register("lock-two@example.com", "ivan", "hash", roleB);
        var c = User.Register("lock-three@example.com", "Ivan", "hash", await third.Roles.SingleAsync(Ct));
        first.Users.Add(a);
        await first.SaveChangesAsync(Ct);
        second.Users.Add(b);
        third.Users.Add(c);
        var waiting = second.SaveChangesAsync(Ct);
        var waitingThird = third.SaveChangesAsync(Ct);
        try
        {
            await LivePostgresServer.WaitForAdvisoryWaitersAsync(fixture.ConnectionString, LivePostgresServer.AccountLockKey, 2, Ct);
            waiting.IsCompleted.Should().BeFalse();
            waitingThird.IsCompleted.Should().BeFalse();
            await transaction.CommitAsync(Ct);
            await waiting;
            await waitingThird;
        }
        finally
        {
            await transaction.DisposeAsync();
            try { await waiting; } catch { }
            try { await waitingThird; } catch { }
        }
        await using var check = fixture.Context();
        (await check.UserPublicAddresses.SingleAsync(x => x.OwnerId == a.Id, Ct)).Slug.Should().Be("ivan");
        (await check.UserPublicAddresses.SingleAsync(x => x.OwnerId == b.Id, Ct)).Slug.Should().Be(PublicSlug.Candidate("ivan", b.Id, 1));
        (await check.UserPublicAddresses.SingleAsync(x => x.OwnerId == c.Id, Ct)).Slug.Should().Be(PublicSlug.Candidate("Ivan", c.Id, 1));
    }

    [Fact]
    public async Task Username_AtoBtoA_RestoresOriginalRowAndRevision()
    {
        var id = await fixture.RegisterAsync("Restore.Alpha", true, Ct);
        await using var db = fixture.Context();
        var original = await db.UserPublicAddresses.AsNoTracking().SingleAsync(x => x.OwnerId == id, Ct);
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        await UpdateUsernameHandler.Handle(new(id, "Restore.Beta"), new UserRepository(db), new UnitOfWork<AccountDbContext>(db), Ct);
        await UpdateUsernameHandler.Handle(new(id, "Restore.Alpha"), new UserRepository(db), new UnitOfWork<AccountDbContext>(db), Ct);
        await transaction.CommitAsync(Ct);
        var rows = await db.UserPublicAddresses.AsNoTracking().Where(x => x.OwnerId == id).ToArrayAsync(Ct);
        rows.Should().HaveCount(2);
        rows.Single(x => x.State == PublicAddressState.Canonical).Id.Should().Be(original.Id);
        rows.Single(x => x.State == PublicAddressState.Canonical).Revision.Should().Be(3);
    }

    [Fact]
    public async Task UsernameConstraintFailure_LeavesOriginalAddressAndAmbientTransactionUsable()
    {
        var id = await fixture.RegisterAsync("Unique.Original", true, Ct);
        await fixture.RegisterAsync("Unique.Occupied", true, Ct);
        await using var db = fixture.Context();
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        var error = await Assert.ThrowsAsync<ConflictException>(() => UpdateUsernameHandler.Handle(new(id, "Unique.Occupied"),
            new UserRepository(db), new UnitOfWork<AccountDbContext>(db), Ct));
        LivePostgresServer.FindPostgres(error)!.SqlState.Should().Be("23505");
        LivePostgresServer.FindPostgres(error)!.ConstraintName.Should().Be("IX_Users_Username");
        (await db.Users.AsNoTracking().SingleAsync(x => x.Id == id, Ct)).Username.Value.Should().Be("Unique.Original");
        var row = await db.UserPublicAddresses.AsNoTracking().SingleAsync(x => x.OwnerId == id, Ct);
        row.Slug.Should().Be("unique-original");
        row.Revision.Should().Be(1);
        row.State.Should().Be(PublicAddressState.Canonical);
        await transaction.CommitAsync(Ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InFlightUsernameConflict_RollsBackUsernameAndDemotion(bool ambient)
    {
        var prefix = ambient ? "Ambient" : "Owned";
        var id = await fixture.RegisterAsync(prefix + ".Original", true, Ct);
        await using var db = fixture.Context();
        await using var transaction = ambient ? await db.Database.BeginTransactionAsync(Ct) : null;
        await using var blocker = await LivePostgresServer.BeginUncommittedTombstoneAsync(fixture.ConnectionString,
            "UserPublicAddresses", PublicSlug.Generate(prefix + ".Conflict"), Ct);
        var waiting = UpdateUsernameHandler.Handle(new(id, prefix + ".Conflict"),
            new UserRepository(db), new UnitOfWork<AccountDbContext>(db), Ct);
        try
        {
            await LivePostgresServer.WaitForLockWaitsAsync(fixture.ConnectionString, "transactionid", 1, Ct);
            waiting.IsCompleted.Should().BeFalse();
            await blocker.CommitAsync(Ct);
            var error = await Assert.ThrowsAsync<ConflictException>(() => waiting);
            LivePostgresServer.FindPostgres(error)!.SqlState.Should().Be("23505");
            LivePostgresServer.FindPostgres(error)!.ConstraintName.Should().Be("IX_UserPublicAddresses_Slug");
        }
        finally
        {
            await blocker.DisposeAsync();
            try { await waiting; } catch { }
        }
        db.ChangeTracker.Entries().Should().BeEmpty();
        (await db.Users.AsNoTracking().SingleAsync(x => x.Id == id, Ct)).Username.Value.Should().Be(prefix + ".Original");
        var row = await db.UserPublicAddresses.AsNoTracking().SingleAsync(x => x.OwnerId == id, Ct);
        row.State.Should().Be(PublicAddressState.Canonical);
        row.Revision.Should().Be(1);
        if (transaction is not null)
        {
            var retry = await db.Users.SingleAsync(x => x.Id == id, Ct);
            retry.UpdateUsername(Username.Create(prefix + ".Retry"));
            await db.SaveChangesAsync(Ct);
            await transaction.CommitAsync(Ct);
        }
    }

    [Fact]
    public async Task DuplicateEmail_RollsBackAddressAndAllowsGoogleRetryAfterClear()
    {
        const string email = "google-retry@example.com";
        var originalId = await fixture.RegisterAsync("Email.Owner", true, Ct, email);
        await using var db = fixture.Context();
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        var failed = User.CreateExternal(email, "Google", "live-google", "Google.Failed");
        db.Users.Add(failed);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        LivePostgresServer.FindPostgres(error)!.SqlState.Should().Be("23505");
        LivePostgresServer.FindPostgres(error)!.ConstraintName.Should().Be("IX_Users_Email");
        db.ChangeTracker.Entries().Should().BeEmpty();
        (await db.UserPublicAddresses.AnyAsync(x => x.OwnerId == failed.Id, Ct)).Should().BeFalse();
        var retry = User.CreateExternal("google-success@example.com", "Google", "live-google-retry", "Google.Retry");
        db.Users.Add(retry);
        await db.SaveChangesAsync(Ct);
        await transaction.CommitAsync(Ct);
        await using var check = fixture.Context();
        (await check.Users.CountAsync(x => x.Credentials.Email == email, Ct)).Should().Be(1);
        var original = await check.UserPublicAddresses.SingleAsync(x => x.OwnerId == originalId, Ct);
        original.Slug.Should().Be("email-owner");
        original.Revision.Should().Be(1);
        (await check.UserPublicAddresses.SingleAsync(x => x.OwnerId == retry.Id, Ct)).Slug.Should().Be("google-retry");
    }
}
