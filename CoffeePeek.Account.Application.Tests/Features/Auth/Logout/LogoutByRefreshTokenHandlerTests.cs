using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Features.Auth.Logout;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.Auth.Logout;

public class LogoutByRefreshTokenHandlerTests
{
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    [Fact]
    public async Task Handle_WithKnownToken_RevokesSessionAndSaves()
    {
        var role = Role.Create("User");
        var user = DomainUser.Register("user@example.com", "user", "hash", role);
        user.AddSession("refresh", TimeSpan.FromDays(7), "Android", "127.0.0.1");
        _userRepository
            .Setup(repository => repository.GetByRefreshToken("refresh", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        await LogoutByRefreshTokenHandler.Handle(
            new LogoutByRefreshTokenCommand("refresh"),
            _userRepository.Object,
            _unitOfWork.Object,
            CancellationToken.None);

        user.RefreshTokens.Single().IsActive.Should().BeFalse();
        _unitOfWork.Verify(unit => unit.SaveChangesAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task Handle_WithUnknownToken_ThrowsUnauthorized()
    {
        _userRepository
            .Setup(repository => repository.GetByRefreshToken("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DomainUser?)null);

        var act = () => LogoutByRefreshTokenHandler.Handle(
            new LogoutByRefreshTokenCommand("missing"),
            _userRepository.Object,
            _unitOfWork.Object,
            CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>();
    }
}
