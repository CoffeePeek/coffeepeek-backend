using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.Admin.Users.Sessions;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.Admin.Users.Sessions;

public class AdminUserSessionsHandlerTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<ISessionTerminationNotifier> _sessionNotifierMock = new();
    private readonly Mock<IRevokedUserStore> _revokedUserStoreMock = new();
    private readonly IOptions<JWTOptions> _jwtOptions = Options.Create(new JWTOptions { AccessTokenLifetimeMinutes = 60 });
    private readonly CancellationToken _ct = CancellationToken.None;

    public AdminUserSessionsHandlerTests()
    {
        _revokedUserStoreMock
            .Setup(x => x.RevokeAsync(It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private static DomainUser CreateUserWithSessions()
    {
        var role = Role.Create("User");
        var user = DomainUser.Register("user@example.com", "testuser", "hash", role);
        user.AddSession("token-a", TimeSpan.FromDays(7), "Chrome", "127.0.0.1");
        user.AddSession("token-b", TimeSpan.FromDays(7), "Safari", "10.0.0.1");
        return user;
    }

    [Fact]
    public async Task GetSessions_ReturnsMetadataWithoutTokenValues()
    {
        var user = CreateUserWithSessions();
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);

        var response = await GetAdminUserSessionsHandler.Handle(
            new GetAdminUserSessionsQuery(user.Id),
            _userRepoMock.Object,
            _ct);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Sessions.Should().HaveCount(2);
        response.Data.Sessions.Should().OnlyContain(s =>
            s.Id != Guid.Empty &&
            !string.IsNullOrWhiteSpace(s.DeviceName) &&
            !string.IsNullOrWhiteSpace(s.IpAddress));
    }

    [Fact]
    public async Task GetSessions_WhenUserMissing_ReturnsNotFound()
    {
        _userRepoMock.Setup(r => r.GetById(It.IsAny<Guid>(), _ct)).ReturnsAsync((DomainUser?)null);

        var response = await GetAdminUserSessionsHandler.Handle(
            new GetAdminUserSessionsQuery(Guid.NewGuid()),
            _userRepoMock.Object,
            _ct);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RevokeSession_RevokesMatchingSession()
    {
        var user = CreateUserWithSessions();
        var sessionId = user.RefreshTokens.First().Id;
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);

        var response = await RevokeAdminUserSessionHandler.Handle(
            new RevokeAdminUserSessionCommand(user.Id, sessionId),
            _userRepoMock.Object,
            _unitOfWorkMock.Object,
            _sessionNotifierMock.Object,
            _ct);

        response.IsSuccess.Should().BeTrue();
        user.RefreshTokens.Single(t => t.Id == sessionId).IsRevoked.Should().BeTrue();
        user.RefreshTokens.Count(t => t.IsActive).Should().Be(1);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
        _sessionNotifierMock.Verify(
            x => x.NotifyForceLogoutAsync(user.Id, SessionTerminationReasons.SessionRevoked, _ct),
            Times.Once);
    }

    [Fact]
    public async Task RevokeSession_WhenSessionMissing_ReturnsNotFound()
    {
        var user = CreateUserWithSessions();
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);

        var response = await RevokeAdminUserSessionHandler.Handle(
            new RevokeAdminUserSessionCommand(user.Id, Guid.NewGuid()),
            _userRepoMock.Object,
            _unitOfWorkMock.Object,
            _sessionNotifierMock.Object,
            _ct);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RevokeAllSessions_RevokesEverySession()
    {
        var user = CreateUserWithSessions();
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);

        var response = await RevokeAllAdminUserSessionsHandler.Handle(
            new RevokeAllAdminUserSessionsCommand(user.Id),
            _userRepoMock.Object,
            _unitOfWorkMock.Object,
            _sessionNotifierMock.Object,
            _revokedUserStoreMock.Object,
            _jwtOptions,
            _ct);

        response.IsSuccess.Should().BeTrue();
        user.RefreshTokens.Should().OnlyContain(t => t.IsRevoked);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
        _sessionNotifierMock.Verify(
            x => x.NotifyForceLogoutAsync(user.Id, SessionTerminationReasons.AllSessionsRevoked, _ct),
            Times.Once);
        _revokedUserStoreMock.Verify(x => x.RevokeAsync(user.Id, TimeSpan.FromMinutes(60), _ct), Times.Once);
    }
}
