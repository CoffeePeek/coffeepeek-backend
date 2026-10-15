using System.Reflection;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.ProfileActivity;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.Profiles;
using CoffeePeek.ShopsService.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace CoffeePeek.ShopsService.Tests;

public sealed class ProfilesActivityControllerTests
{
    [Fact]
    public async Task ResolvesPublicProfileAndReturnsCalendarWithoutInternalIdentity()
    {
        var ct = TestContext.Current.CancellationToken;
        var author = new PublicAddressMetadata(Guid.NewGuid(), "glasha", "/users/glasha", 1);
        var profiles = new Mock<IProfileAddressLookup>();
        profiles.Setup(lookup => lookup.ResolveAsync(author.Slug, ct)).ReturnsAsync(author);
        var activity = new Mock<IProfileActivityQueries>();
        var calendar = new ProfileActivityDto(2025, 9, "Europe/Minsk", 1, 2, [new(new DateOnly(2025, 9, 1), 2)]);
        activity.Setup(queries => queries.GetAsync(author.EntityId,
            It.Is<ProfileActivityMonth>(period => period.Year == 2025 && period.Month == 9 && period.TimeZone.Id == "Europe/Minsk"), ct))
            .ReturnsAsync(calendar);

        var result = Assert.IsType<OkObjectResult>(await new ProfilesActivityController(profiles.Object, activity.Object)
            .Get("glasha", 2025, 9, "Europe/Minsk", ct));

        Assert.Same(calendar, Assert.IsType<Response<ProfileActivityDto>>(result.Value).Data);
        var json = JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"date\":\"2025-09-01\"", json);
        Assert.DoesNotContain(author.EntityId.ToString(), json);
        activity.VerifyAll();
    }

    [Fact]
    public async Task UnknownOrUnavailableProfileDoesNotReadVisits()
    {
        var profiles = new Mock<IProfileAddressLookup>();
        var activity = new Mock<IProfileActivityQueries>(MockBehavior.Strict);
        var result = await new ProfilesActivityController(profiles.Object, activity.Object)
            .Get("missing", 2025, 9, ct: TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundObjectResult>(result);
        activity.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ValidatesPeriodBeforeLookingUpProfile()
    {
        var profiles = new Mock<IProfileAddressLookup>(MockBehavior.Strict);
        var activity = new Mock<IProfileActivityQueries>(MockBehavior.Strict);
        await Assert.ThrowsAsync<ValidationException>(() => new ProfilesActivityController(profiles.Object, activity.Object)
            .Get("glasha", 2025, 13, ct: TestContext.Current.CancellationToken));
        profiles.VerifyNoOtherCalls();
        activity.VerifyNoOtherCalls();
    }

    [Fact]
    public void AnonymousCalendarDisablesSharedCaching()
    {
        var controller = typeof(ProfilesActivityController);
        Assert.Equal("api/v1/profiles/{authorSlug}/activity", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.True(controller.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        Assert.NotNull(controller.GetMethod("Get")!.GetCustomAttribute<AllowAnonymousAttribute>());
    }
}
