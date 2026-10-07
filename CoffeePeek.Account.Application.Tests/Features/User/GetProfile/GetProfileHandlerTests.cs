using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Features.User.GetProfile;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using MapsterMapper;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Application.Tests.Features.User.GetProfile;

public class GetProfileHandlerTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private static DomainUser CreateUser()
    {
        var role = Role.Create("User");
        return DomainUser.Register("user@example.com", "testuser", "hash", role);
    }

    [Fact]
    public async Task Handle_WhenUserExists_ReturnsProfileWithEmail()
    {
        var user = CreateUser();
        var profile = new UserProfileResponse(
            "testuser", "user@example.com", DateTime.UtcNow, null, null, 0, 0);
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        _mapperMock.Setup(m => m.Map<UserProfileResponse>(user)).Returns(profile);

        var result = await GetProfileHandler.Handle(
            new GetProfileCommand(user.Id), _userRepoMock.Object, _mapperMock.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Email.Should().Be("user@example.com");
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ThrowsNotFoundException()
    {
        _userRepoMock.Setup(r => r.GetById(It.IsAny<Guid>(), _ct)).ReturnsAsync((DomainUser?)null);

        Func<Task> act = () => GetProfileHandler.Handle(
            new GetProfileCommand(Guid.NewGuid()), _userRepoMock.Object, _mapperMock.Object, _ct);

        await act.Should().ThrowAsync<NotFoundException>().WithMessage("User not found");
    }
}
