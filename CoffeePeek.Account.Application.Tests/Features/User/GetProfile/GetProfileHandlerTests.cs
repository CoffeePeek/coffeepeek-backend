using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Account.Application.Features.User.GetProfile;
using CoffeePeek.Account.Domain.Entities.RoleAggregate;
using CoffeePeek.Account.Domain.Entities.UserAggregate;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using CoffeePeek.Shared.Kernel.Options;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Contract.Dtos.Profiles;

namespace CoffeePeek.Account.Application.Tests.Features.User.GetProfile;

public class GetProfileHandlerTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly IOptions<MediaPublicUrlOptions> _media = Options.Create(new MediaPublicUrlOptions());
    private readonly Mock<IProfileSocialStore> _social = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    public GetProfileHandlerTests() => _social.Setup(s => s.GetStateAsync(It.IsAny<Guid>(), null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ProfileFollowStateDto(0, false));

    private static DomainUser CreateUser()
    {
        var role = Role.Create("User");
        return DomainUser.Register("user@example.com", "testuser", "hash", role);
    }

    [Fact]
    public async Task Handle_WhenUserExists_ReturnsProfileWithEmail()
    {
        var user = CreateUser();
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);

        var result = await GetProfileHandler.Handle(
            new GetProfileCommand(user.Id), _userRepoMock.Object, _media, _social.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Email.Should().Be("user@example.com");
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ThrowsNotFoundException()
    {
        _userRepoMock.Setup(r => r.GetById(It.IsAny<Guid>(), _ct)).ReturnsAsync((DomainUser?)null);

        Func<Task> act = () => GetProfileHandler.Handle(
            new GetProfileCommand(Guid.NewGuid()), _userRepoMock.Object, _media, _social.Object, _ct);

        await act.Should().ThrowAsync<NotFoundException>().WithMessage("User not found");
    }
}
