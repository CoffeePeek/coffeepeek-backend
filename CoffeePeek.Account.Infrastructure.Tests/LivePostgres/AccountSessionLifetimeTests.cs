using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.Auth.RefreshToken;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Account.Persistence.Repositories;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace CoffeePeek.Account.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class AccountSessionLifetimeTests : IClassFixture<AccountSessionMigrationDatabase>
{
    private readonly AccountSessionMigrationDatabase fixture;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public AccountSessionLifetimeTests(AccountSessionMigrationDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task Upgrade_BackfillsAbsoluteDeadlineWithoutRevivingExpiredOrRevokedTokens()
    {
        await using var db = fixture.Context();
        var tokens = await db.RefreshTokens.Where(t => t.Token.StartsWith("legacy-")).ToListAsync(Ct);

        tokens.Should().HaveCount(3);
        foreach (var token in tokens)
        {
            token.SessionExpiresAtUtc.Should().Be(token.CreatedAtUtc.AddDays(365));
            token.ExpiryDate.Should().Be(token.CreatedAtUtc.AddDays(7));
        }
        tokens.Single(t => t.Token == "legacy-active").IsActive.Should().BeTrue();
        tokens.Single(t => t.Token == "legacy-expired").IsActive.Should().BeFalse();
        tokens.Single(t => t.Token == "legacy-revoked").IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task Rotation_AfterReload_PreservesAbsoluteDeadlineAndReturnsCappedExpiry()
    {
        await using var db = fixture.Context();
        var user = User.CreateExternal("rotation@example.com", "Google", "rotation");
        user.AddSession("near-limit", TimeSpan.FromDays(90), "device", "ip", TimeSpan.FromDays(1));
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
        var original = await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.Token == "near-limit", Ct);
        var jwt = new Mock<IJWTTokenService>();
        jwt.Setup(x => x.GenerateRefreshToken()).Returns("rotated");
        jwt.Setup(x => x.GenerateAccessToken(It.IsAny<User>())).Returns("access");

        var result = await RefreshTokenHandler.Handle(new("near-limit"), new UserRepository(db), jwt.Object,
            MsOptions.Create(new JWTOptions()), Mock.Of<IRevokedUserStore>(),
            new UnitOfWork<AccountDbContext>(db), Ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.RefreshTokenExpiresAt.Should().Be(original.SessionExpiresAtUtc);
        db.ChangeTracker.Clear();
        var rotated = await db.RefreshTokens.SingleAsync(t => t.Token == "rotated", Ct);
        rotated.SessionExpiresAtUtc.Should().Be(original.SessionExpiresAtUtc);
        rotated.ExpiryDate.Should().Be(original.SessionExpiresAtUtc);
        (await db.RefreshTokens.SingleAsync(t => t.Token == "near-limit", Ct)).IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task ExpiredRefresh_LeavesOtherDeviceAbleToRefresh()
    {
        await using var db = fixture.Context();
        var user = User.CreateExternal("expiry@example.com", "Google", "expiry");
        user.AddSession("expired", TimeSpan.FromDays(-1), "old-device", "ip");
        user.AddSession("other-device", TimeSpan.FromDays(90), "phone", "ip");
        db.Users.Add(user);
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
        var revoked = new Mock<IRevokedUserStore>();
        var jwt = new Mock<IJWTTokenService>();
        jwt.Setup(x => x.GenerateRefreshToken()).Returns("other-device-rotated");
        jwt.Setup(x => x.GenerateAccessToken(It.IsAny<User>())).Returns("access");
        var options = MsOptions.Create(new JWTOptions());
        var repository = new UserRepository(db);
        var uow = new UnitOfWork<AccountDbContext>(db);

        var expired = await RefreshTokenHandler.Handle(new("expired"), repository, jwt.Object,
            options, revoked.Object, uow, Ct);
        expired.StatusCode.Should().Be(401);
        expired.Message.Should().Be(RefreshTokenHandler.TokenExpiredMessage);
        db.ChangeTracker.Clear();
        var other = await RefreshTokenHandler.Handle(new("other-device"), repository, jwt.Object,
            options, revoked.Object, uow, Ct);

        other.IsSuccess.Should().BeTrue();
        revoked.Invocations.Should().BeEmpty();
        db.ChangeTracker.Clear();
        (await db.RefreshTokens.SingleAsync(t => t.Token == "other-device-rotated", Ct)).IsActive.Should().BeTrue();
    }
}
