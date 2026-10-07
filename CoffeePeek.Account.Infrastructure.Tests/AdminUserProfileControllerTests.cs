using System.Reflection;
using CoffeePeek.AccountService.Controllers;
using CoffeePeek.Account.Application.Features.User.GetProfile;
using CoffeePeek.Shared.Auth.Constants;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Kernel.Exceptions;
using Moq;
using Wolverine;

namespace CoffeePeek.Account.Infrastructure.Tests;

public sealed class AdminUserProfileControllerTests
{
    [Fact]
    public async Task GetProfile_DispatchesExactUuidAndReturnsPublicProfile()
    {
        var id = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        var bus = new Mock<IMessageBus>();
        var profile = new PublicUserProfileResponse("Author", DateTime.UtcNow, "About", null, 2, 3);
        bus.Setup(b => b.InvokeAsync<Response<PublicUserProfileResponse>>(
            It.Is<object>(m => m is GetPublicUserProfileCommand && ((GetPublicUserProfileCommand)m).UserId == id), ct, null))
            .ReturnsAsync(Response<PublicUserProfileResponse>.Success(profile));
        var result = await new AdminUserProfilesController(bus.Object).GetProfile(id, ct);
        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<Response<PublicUserProfileResponse>>()
            .Which.Data.Should().BeSameAs(profile);
        bus.VerifyAll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetProfile_HiddenOrMissing_ReturnsIdenticalGenericNotFound(bool throws)
    {
        var bus = new Mock<IMessageBus>();
        var setup = bus.Setup(b => b.InvokeAsync<Response<PublicUserProfileResponse>>(It.IsAny<object>(), It.IsAny<CancellationToken>(), null));
        if (throws) setup.ThrowsAsync(new NotFoundException("blocked/deleted"));
        else setup.ReturnsAsync(Response<PublicUserProfileResponse>.Error("missing"));
        (await new AdminUserProfilesController(bus.Object).GetProfile(Guid.NewGuid(), TestContext.Current.CancellationToken))
            .Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public void ProfileRead_IsProtectedPublicOnlyAndUncached()
    {
        var type = typeof(UsersController).Assembly.GetType("CoffeePeek.AccountService.Controllers.AdminUserProfilesController");
        type.Should().NotBeNull();
        type!.GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/admin/users");
        type.GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(RoleConsts.Moderator);
        type.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore.Should().BeTrue();
        type.GetMethods().SelectMany(m => m.GetCustomAttributes<HttpGetAttribute>()).Single().Template.Should().Be("{id:guid}/profile");
        typeof(PublicUserProfileResponse).GetProperty("Email").Should().BeNull();
        typeof(PublicUserProfileResponse).GetProperty("PhoneNumber").Should().BeNull();
        typeof(AdminUsersController).GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(RoleConsts.Admin);
    }
}
