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
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.Admin.Users;
using CoffeePeek.Account.Application.Features.User.DeleteUser;
using CoffeePeek.Account.Domain.Entities.AccountDeletionRequestAggregate;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using MsOptions = Microsoft.Extensions.Options.Options;
using Moq;

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deletion_DoesNotHoldAddressLockDuringRedisOrSignalR(bool admin)
    {
        var prefix = admin ? "AdminDelete" : "ConfirmedDelete";
        var id = await fixture.RegisterAsync(prefix + ".Owner", true, Ct);
        await using var db = fixture.Context();
        var email = (await db.Users.AsNoTracking().SingleAsync(x => x.Id == id, Ct)).Credentials.Email.Value;
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        var redisEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var redisRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var signalEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var signalRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var revoked = new Mock<IRevokedUserStore>();
        revoked.Setup(x => x.RevokeAsync(id, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(() => { redisEntered.TrySetResult(); return redisRelease.Task; });
        var notifier = new Mock<ISessionTerminationNotifier>();
        notifier.Setup(x => x.NotifyForceLogoutAsync(id, SessionTerminationReasons.UserDeleted, It.IsAny<CancellationToken>()))
            .Returns(() => { signalEntered.TrySetResult(); return signalRelease.Task; });
        var jwt = MsOptions.Create(new JWTOptions { AccessTokenLifetimeMinutes = 60 });
        var users = new UserRepository(db);
        var uow = new UnitOfWork<AccountDbContext>(db);
        Task deletion;
        if (admin)
            deletion = AdminDeleteUserHandler.Handle(new(id, Guid.NewGuid()), users, uow, notifier.Object, revoked.Object, jwt, Ct);
        else
        {
            var request = AccountDeletionRequest.Create(id, "hash", DateTime.UtcNow, TimeSpan.FromHours(24), TimeSpan.FromMinutes(5));
            db.AccountDeletionRequests.Add(request);
            // Persist the request before the handler; this write does not acquire the profile-address lock.
            await db.SaveChangesAsync(Ct);
            var requests = new Mock<IAccountDeletionRequestRepository>();
            requests.Setup(x => x.GetByTokenHash("hash", It.IsAny<CancellationToken>())).ReturnsAsync(request);
            var tokens = new Mock<IAccountDeletionTokenService>();
            tokens.Setup(x => x.Hash("token")).Returns("hash");
            deletion = ConfirmAccountDeletionHandler.Handle(new("token", AccountDeletionReason.PrivacyConcerns),
                requests.Object, tokens.Object, users, revoked.Object, notifier.Object, jwt, TimeProvider.System, uow, Ct);
        }
        var registrations = new List<Task<Guid>>();
        try
        {
            await redisEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            registrations.Add(fixture.RegisterAsync(prefix + ".DuringRedis", true, Ct));
            await registrations[^1].WaitAsync(TimeSpan.FromSeconds(10), Ct);
            redisRelease.TrySetResult(true);
            await signalEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
            registrations.Add(fixture.RegisterAsync(prefix + ".DuringSignal", true, Ct));
            await registrations[^1].WaitAsync(TimeSpan.FromSeconds(10), Ct);
            signalRelease.TrySetResult();
            await deletion;
            await transaction.CommitAsync(Ct);
        }
        finally
        {
            redisRelease.TrySetResult(true);
            signalRelease.TrySetResult();
            try { await deletion; } catch { }
            await transaction.DisposeAsync();
            foreach (var pending in registrations) try { await pending; } catch { }
        }
        await using var check = fixture.Context();
        (await check.Users.AnyAsync(x => x.Id == id, Ct)).Should().BeFalse();

        var replacementId = await fixture.RegisterAsync(prefix + ".Replacement", true, Ct, email);
        (await check.Users.CountAsync(x => x.Credentials.Email == email, Ct)).Should().Be(1);
        replacementId.Should().NotBe(id);
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
        (await check.Users.SingleAsync(x => x.Id == a.Id, Ct)).PublicSlug.Should().Be("ivan");
        (await check.Users.SingleAsync(x => x.Id == b.Id, Ct)).PublicSlug.Should().Be(PublicSlug.Candidate("ivan", b.Id, 1));
        (await check.Users.SingleAsync(x => x.Id == c.Id, Ct)).PublicSlug.Should().Be(PublicSlug.Candidate("Ivan", c.Id, 1));
    }

    [Fact]
    public async Task Username_AtoBtoA_UpdatesOnlyCurrentSlug()
    {
        var id = await fixture.RegisterAsync("Restore.Alpha", true, Ct);
        await using var db = fixture.Context();
        var original = await db.Users.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        await using var transaction = await db.Database.BeginTransactionAsync(Ct);
        await UpdateUsernameHandler.Handle(new(id, "Restore.Beta"), new UserRepository(db), new UnitOfWork<AccountDbContext>(db), Ct);
        // Model a second change after the cooldown without waiting seven real days.
        var previousChange = DateTime.UtcNow.AddDays(-7);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"UsernameChangedAtUtc\" = {previousChange} WHERE \"Id\" = {id}", Ct);
        db.ChangeTracker.Clear();
        await UpdateUsernameHandler.Handle(new(id, "Restore.Alpha"), new UserRepository(db), new UnitOfWork<AccountDbContext>(db), Ct);
        await transaction.CommitAsync(Ct);
        var saved = await db.Users.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        saved.PublicSlug.Should().Be(original.PublicSlug);
        saved.SlugRevision.Should().Be(3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UsernameCooldown_RejectsStaleConcurrentRenameWithFlagOnOrOff(bool enabled)
    {
        var prefix = enabled ? "Cooldown.On" : "Cooldown.Off";
        var id = await fixture.RegisterAsync(prefix, enabled, Ct);
        await using var first = fixture.Context(enabled);
        await using var stale = fixture.Context(enabled);
        var a = await first.Users.SingleAsync(x => x.Id == id, Ct);
        var b = await stale.Users.SingleAsync(x => x.Id == id, Ct);
        a.UpdateUsername(Username.Create(prefix + ".First"));
        b.UpdateUsername(Username.Create(prefix + ".Second"));
        await first.SaveChangesAsync(Ct);
        var error = await Assert.ThrowsAsync<ConflictException>(() => new UnitOfWork<AccountDbContext>(stale).SaveChangesAsync(Ct));
        error.InnerException.Should().BeOfType<DbUpdateConcurrencyException>();
        await using var check = fixture.Context(enabled);
        var saved = await check.Users.SingleAsync(x => x.Id == id, Ct);
        saved.Username.Value.Should().Be(prefix + ".First");
        saved.UsernameChangedAtUtc.Should().NotBeNull();
        Assert.Throws<ConflictException>(() => saved.UpdateUsername(Username.Create(prefix + ".Retry")));
        saved.PublicSlug.Should().Be(enabled ? PublicSlug.Generate(prefix + ".First") : null);
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
        var row = await db.Users.AsNoTracking().SingleAsync(x => x.Id == id, Ct);
        row.PublicSlug.Should().Be("unique-original");
        row.SlugRevision.Should().Be(1);
        await transaction.CommitAsync(Ct);
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
        (await db.Users.AnyAsync(x => x.Id == failed.Id, Ct)).Should().BeFalse();
        var retry = User.CreateExternal("google-success@example.com", "Google", "live-google-retry", "Google.Retry");
        db.Users.Add(retry);
        await db.SaveChangesAsync(Ct);
        await transaction.CommitAsync(Ct);
        await using var check = fixture.Context();
        (await check.Users.CountAsync(x => x.Credentials.Email == email, Ct)).Should().Be(1);
        var original = await check.Users.SingleAsync(x => x.Id == originalId, Ct);
        original.PublicSlug.Should().Be("email-owner");
        original.SlugRevision.Should().Be(1);
        (await check.Users.SingleAsync(x => x.Id == retry.Id, Ct)).PublicSlug.Should().Be("google-retry");
    }
}
