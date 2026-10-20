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

public class GetPublicUserProfileHandlerTests
{
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly IOptions<MediaPublicUrlOptions> _media = Options.Create(new MediaPublicUrlOptions());
    private readonly Mock<IProfileSocialStore> _social = new();
    private readonly GetPublicUserProfileHandler _handler = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    public GetPublicUserProfileHandlerTests() => _social.Setup(s => s.GetStateAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new ProfileFollowStateDto(0, false));

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
            "testuser", user.CreatedAtUtc, null, null, 0, 0);
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);

        var result = await _handler.Handle(
            new GetPublicUserProfileCommand(user.Id), _userRepoMock.Object, _media, _social.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(profile with { AuthorId = user.PublicAuthorId });
        result.Data!.AuthorId.Should().NotBe(user.Id);
        result.Data!.GetType().GetProperty("Email").Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ThrowsNotFoundException()
    {
        _userRepoMock.Setup(r => r.GetById(It.IsAny<Guid>(), _ct)).ReturnsAsync((DomainUser?)null);

        Func<Task> act = () => _handler.Handle(
            new GetPublicUserProfileCommand(Guid.NewGuid()), _userRepoMock.Object, _media, _social.Object, _ct);

        await act.Should().ThrowAsync<NotFoundException>().WithMessage("User not found");
    }

    [Fact]
    public async Task Handle_ReturnsLiveFollowStateAndRoasterContributionsForViewer()
    {
        var user = CreateUser();
        user.Statistics.IncrementAddedRoasters();
        user.Statistics.IncrementCheckIn();
        user.Statistics.IncrementAddedShops();
        var viewerId = Guid.NewGuid();
        _userRepoMock.Setup(r => r.GetById(user.Id, _ct)).ReturnsAsync(user);
        _social.Setup(s => s.GetStateAsync(user.Id, viewerId, _ct)).ReturnsAsync(new ProfileFollowStateDto(42, true));

        var result = await _handler.Handle(new GetPublicUserProfileCommand(user.Id) { ViewerId = viewerId },
            _userRepoMock.Object, _media, _social.Object, _ct);

        result.Data!.AddedRoastersCount.Should().Be(1);
        result.Data.FollowersCount.Should().Be(42);
        result.Data.IsFollowing.Should().BeTrue();
        result.Data.CheckInCount.Should().Be(1);
        result.Data.AddedShopsCount.Should().Be(1);
    }

    // D-04: deleted and blocked profiles are not public and must be indistinguishable from unknown ids.
    // The user exists, so without the visibility check the handler would return the profile.
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

        Func<Task> act = () => _handler.Handle(
            new GetPublicUserProfileCommand(user.Id), _userRepoMock.Object, _media, _social.Object, _ct);

        await act.Should().ThrowAsync<NotFoundException>().WithMessage("User not found");
    }
}
