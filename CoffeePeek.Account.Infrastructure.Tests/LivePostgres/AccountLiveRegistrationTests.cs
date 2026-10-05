using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.Auth.OAuthLogin;
using CoffeePeek.Account.Infrastructure.Identity;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Account.Persistence.Repositories;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Testing;
using FluentAssertions;
using Google.Apis.Auth;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CoffeePeek.Account.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class AccountLiveRegistrationTests : IClassFixture<AccountLiveDatabase>
{
    private readonly AccountLiveDatabase fixture;
    public AccountLiveRegistrationTests(AccountLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task GoogleConcurrentCreation_RetriesAfterChangeTrackerClear()
    {
        var ct = TestContext.Current.CancellationToken;
        var google = new Mock<IGoogleAuthService>();
        google.Setup(x => x.ValidateIdTokenAsync("live-token", ct)).ReturnsAsync(new GoogleJsonWebSignature.Payload
        {
            Email = "gseven@example.com", EmailVerified = true, Subject = "live-google-concurrent"
        });
        var options = Microsoft.Extensions.Options.Options.Create(new JWTOptions
        {
            SecretKey = "live-gate-secret-key-at-least-32-chars!!", Issuer = "live-gate", Audience = "live-gate",
            AccessTokenLifetimeMinutes = 15, RefreshTokenLifetimeDays = 30
        });
        Task<Response<GoogleLoginResponse>> Login(AccountDbContext db) => GoogleLoginHandler.Handle(new("live-token"),
            google.Object, new ExternalAuthService(new QueryUserRepository(db), new RoleRepository(db)),
            new JWTTokenService(options), options, new UnitOfWork<AccountDbContext>(db), ct);
        await using var first = fixture.Context();
        await using var transaction = await first.Database.BeginTransactionAsync(ct);
        var a = await Login(first);
        a.IsSuccess.Should().BeTrue();
        async Task<Response<GoogleLoginResponse>> Second()
        {
            await using var second = fixture.Context();
            await using var tx = await second.Database.BeginTransactionAsync(ct);
            var response = await Login(second);
            await second.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return response;
        }
        var waiting = Second();
        try
        {
            await LivePostgresServer.WaitForAdvisoryWaitersAsync(fixture.ConnectionString, LivePostgresServer.AccountLockKey, 1, ct);
            waiting.IsCompleted.Should().BeFalse();
            await first.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            (await waiting).IsSuccess.Should().BeTrue();
        }
        finally
        {
            await transaction.DisposeAsync();
            try { await waiting; } catch { }
        }
        // Flag-on writer rolls back the failed second insert and clears tracking before the handler retry.
        await using var check = fixture.Context();
        var users = await check.Users.Include(x => x.RefreshTokens).Where(x => x.Credentials.Email == "gseven@example.com").ToArrayAsync(ct);
        users.Should().ContainSingle();
        users[0].RefreshTokens.Should().HaveCount(2);
        var address = await check.Users.SingleAsync(x => x.PublicSlug!.StartsWith("gseven"), ct);
        address.Id.Should().Be(users[0].Id);
        address.PublicSlug.Should().Be("gseven");
        address.SlugRevision.Should().Be(1);
    }
}
