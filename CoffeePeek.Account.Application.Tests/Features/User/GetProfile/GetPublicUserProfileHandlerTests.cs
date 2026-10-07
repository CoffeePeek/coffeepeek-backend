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

public class GetPublicUserProfileHandlerTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly GetPublicUserProfileHandler _handler = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    private static DomainUser CreateUser()
    {
        var role = Role.Create("User");
        return DomainUser.Register("user@example.com", "testuser", "hash", role);
    }

    [Fact]
    public async Task Handle_WhenUserExists_ReturnsPublicProfileWithoutEmail()
    {
        var user = CreateUser();
        var profile = new PublicUserProfileResponse(
            "testuser", DateTime.UtcNow, null, null, 0, 0);
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        _mapperMock.Setup(m => m.Map<PublicUserProfileResponse>(user)).Returns(profile);

        var result = await _handler.Handle(
            new GetPublicUserProfileCommand(user.Id), _userRepoMock.Object, _mapperMock.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(profile);
        result.Data!.GetType().GetProperty("Email").Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ThrowsNotFoundException()
    {
        _userRepoMock.Setup(r => r.GetById(It.IsAny<Guid>(), _ct)).ReturnsAsync((DomainUser?)null);

        Func<Task> act = () => _handler.Handle(
            new GetPublicUserProfileCommand(Guid.NewGuid()), _userRepoMock.Object, _mapperMock.Object, _ct);

        await act.Should().ThrowAsync<NotFoundException>().WithMessage("User not found");
    }

    // D-04: deleted and blocked profiles are not public and must be indistinguishable from unknown ids. The mapper is
    // set up to succeed, so without the visibility check the handler would return the profile.
    [Fact]
    public async Task Handle_WhenUserSoftDeleted_ThrowsNotFoundAndDoesNotMap()
    {
        var user = CreateUser();
        user.SetSoftDelete();
        await AssertHiddenAsync(user);
    }

    [Fact]
    public async Task Handle_WhenUserBlocked_ThrowsNotFoundAndDoesNotMap()
    {
        var user = CreateUser();
        user.SetBlocked(true);
        await AssertHiddenAsync(user);
    }

    private async Task AssertHiddenAsync(DomainUser user)
    {
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        _mapperMock.Setup(m => m.Map<PublicUserProfileResponse>(It.IsAny<object>()))
            .Returns(new PublicUserProfileResponse("testuser", DateTime.UtcNow, null, null, 0, 0));

        Func<Task> act = () => _handler.Handle(
            new GetPublicUserProfileCommand(user.Id), _userRepoMock.Object, _mapperMock.Object, _ct);

        await act.Should().ThrowAsync<NotFoundException>().WithMessage("User not found");
        _mapperMock.Verify(m => m.Map<PublicUserProfileResponse>(It.IsAny<object>()), Times.Never);
    }
}
