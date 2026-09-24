using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.Admin.Users;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Domain.Interfaces.Persistance;
using CoffeePeek.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.Admin.Users;

public class AdminUserHandlersTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IRoleRepository> _roleRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<ISessionTerminationNotifier> _sessionNotifierMock = new();
    private readonly Mock<IRevokedUserStore> _revokedUserStoreMock = new();
    private readonly IOptions<JWTOptions> _jwtOptions = Options.Create(new JWTOptions { AccessTokenLifetimeMinutes = 60 });
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly DomainUser _target = DomainUser.Register("target@example.com", "target", "hash", Role.Create("Admin"));
    private readonly Guid _actorId = Guid.NewGuid();

    public AdminUserHandlersTests()
    {
        _userRepoMock.Setup(r => r.GetById(_target.Id, _ct)).ReturnsAsync(_target);
        _revokedUserStoreMock
            .Setup(x => x.RevokeAsync(It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private void VerifyAccessTokensRevoked() =>
        _revokedUserStoreMock.Verify(x => x.RevokeAsync(_target.Id, TimeSpan.FromMinutes(60), _ct), Times.Once);

    [Fact]
    public async Task Block_RevokesAccessTokens()
    {
        var response = await BlockUserHandler.Handle(
            new BlockUserCommand(_target.Id, true, _actorId),
            _userRepoMock.Object, _unitOfWorkMock.Object, _sessionNotifierMock.Object,
            _revokedUserStoreMock.Object, _jwtOptions, _ct);

        response.IsSuccess.Should().BeTrue();
        _target.IsBlocked.Should().BeTrue();
        VerifyAccessTokensRevoked();
    }

    [Fact]
    public async Task Block_OwnAccount_IsRejected()
    {
        var response = await BlockUserHandler.Handle(
            new BlockUserCommand(_target.Id, true, _target.Id),
            _userRepoMock.Object, _unitOfWorkMock.Object, _sessionNotifierMock.Object,
            _revokedUserStoreMock.Object, _jwtOptions, _ct);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
        _target.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_RevokesAccessTokens()
    {
        var response = await AdminDeleteUserHandler.Handle(
            new AdminDeleteUserCommand(_target.Id, _actorId),
            _userRepoMock.Object, _unitOfWorkMock.Object, _sessionNotifierMock.Object,
            _revokedUserStoreMock.Object, _jwtOptions, _ct);

        response.IsSuccess.Should().BeTrue();
        _target.IsSoftDelete.Should().BeTrue();
        VerifyAccessTokensRevoked();
    }

    [Fact]
    public async Task Delete_OwnAccount_IsRejected()
    {
        var response = await AdminDeleteUserHandler.Handle(
            new AdminDeleteUserCommand(_target.Id, _target.Id),
            _userRepoMock.Object, _unitOfWorkMock.Object, _sessionNotifierMock.Object,
            _revokedUserStoreMock.Object, _jwtOptions, _ct);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
        _target.IsSoftDelete.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateRole_RevokesAccessTokensCarryingOldRole()
    {
        _roleRepoMock.Setup(r => r.GetRoleAsync("User")).ReturnsAsync(Role.Create("User"));

        var response = await UpdateUserRoleHandler.Handle(
            new UpdateUserRoleCommand(_target.Id, "User", _actorId),
            _userRepoMock.Object, _roleRepoMock.Object, _unitOfWorkMock.Object,
            _revokedUserStoreMock.Object, _jwtOptions, _ct);

        response.IsSuccess.Should().BeTrue();
        VerifyAccessTokensRevoked();
    }

    [Fact]
    public async Task UpdateRole_OwnAccount_IsRejected()
    {
        // Self-demotion is how the last admin disappears.
        var response = await UpdateUserRoleHandler.Handle(
            new UpdateUserRoleCommand(_target.Id, "User", _target.Id),
            _userRepoMock.Object, _roleRepoMock.Object, _unitOfWorkMock.Object,
            _revokedUserStoreMock.Object, _jwtOptions, _ct);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
