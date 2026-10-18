using System.Reflection;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.AccountService.Controllers;
using CoffeePeek.Contract.Dtos.Profiles;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace CoffeePeek.Account.Infrastructure.Tests;

public sealed class ProfileFollowsControllerTests
{
    private readonly Mock<IProfileAddressService> addresses = new();
    private readonly Mock<IProfileSocialStore> social = new();
    private readonly Mock<IUserContext> viewer = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mutation_UsesAuthenticatedActorAndResolvedOwner(bool follow)
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        viewer.Setup(v => v.GetUserIdOrThrow()).Returns(actorId);
        addresses.Setup(a => a.ResolveAsync("owner", Ct)).ReturnsAsync(new PublicAddressMetadata(targetId, "owner", "/users/owner", 1));
        social.Setup(s => s.GetStateAsync(targetId, actorId, Ct)).ReturnsAsync(new ProfileFollowStateDto(follow ? 1 : 0, follow));
        var controller = Controller();

        var result = follow ? await controller.Follow("owner", Ct) : await controller.Unfollow("owner", Ct);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(new ProfileFollowStateDto(follow ? 1 : 0, follow));
        social.Verify(s => s.SetAsync(actorId, targetId, follow, Ct), Times.Once);
    }

    [Fact]
    public async Task HiddenProfile_ReturnsNotFoundBeforeReadingOrWritingRelations()
    {
        (await Controller().Follow("unknown", Ct)).Should().BeOfType<NotFoundResult>();
        (await Controller().Followers("unknown", Ct)).Should().BeOfType<NotFoundResult>();
        social.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProfileHiddenAfterResolve_ReturnsSameEmptyNotFound()
    {
        var targetId = Guid.NewGuid();
        addresses.Setup(a => a.ResolveAsync("owner", Ct)).ReturnsAsync(new PublicAddressMetadata(targetId, "owner", "/users/owner", 1));
        social.Setup(s => s.ListAsync(targetId, false, 2, 10, Ct, null)).ThrowsAsync(new NotFoundException("User not found"));
        (await Controller().Followers("owner", Ct, 2, 10)).Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void WritesRequireAuthenticationAndReadsNeverCache()
    {
        var type = typeof(ProfileFollowsController);
        type.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore.Should().BeTrue();
        foreach (var action in new[] { "Follow", "Unfollow" })
            type.GetMethod(action)!.GetCustomAttributes<AuthorizeAttribute>().Should().ContainSingle();
        foreach (var action in new[] { "Followers", "Following" })
            type.GetMethod(action)!.GetCustomAttributes<AuthorizeAttribute>().Should().BeEmpty();
        typeof(ProfileFollowUserDto).GetProperty("Email").Should().BeNull();
        typeof(ProfileFollowUserDto).GetProperty("PhoneNumber").Should().BeNull();
        typeof(ProfileFollowUserDto).GetProperty("Id").Should().BeNull();
    }

    private ProfileFollowsController Controller() => new(addresses.Object, social.Object, viewer.Object);
}
