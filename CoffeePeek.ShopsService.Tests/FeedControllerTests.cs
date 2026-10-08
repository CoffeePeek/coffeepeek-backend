using System.Reflection;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.Feed;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Feed;
using CoffeePeek.ShopsService.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Wolverine;

namespace CoffeePeek.ShopsService.Tests;

public class FeedControllerTests
{
    [Fact]
    public async Task FeedUsesAuthenticatedReaderAndReturnsPublishedCheckInContract()
    {
        var ct = TestContext.Current.CancellationToken;
        var readerId = Guid.NewGuid();
        var time = DateTime.UtcNow.AddDays(-1);
        var bus = DispatchProxy.Create<IMessageBus, ReviewReportApiContractTests.RecordingBus>();
        var recorder = (ReviewReportApiContractTests.RecordingBus)(object)bus;
        recorder.NextResponse = Response<CheckInFeedPageDto>.Success(new([
            new(time, new PublicCheckInDto { Id = Guid.NewGuid(), Text = "Good coffee", IsHelpfulByCurrentUser = true })], "opaque-cursor"));
        var user = new Mock<IUserContext>(); user.Setup(x => x.GetUserId()).Returns(readerId);
        var controller = new FeedController(bus, user.Object);
        var result = Assert.IsType<OkObjectResult>(await controller.Get("city", "shop", "author", 25, "cursor", ct));
        Assert.Equal(new GetCheckInFeedQuery(readerId, "city", "shop", "author", 25, "cursor"), recorder.LastMessage);
        var response = Assert.IsType<Response<CheckInFeedPageDto>>(result.Value);
        Assert.Equal(time, Assert.Single(response.Data.Items).PublishedAtUtc);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"publishedAtUtc\"", json); Assert.Contains("\"checkIn\"", json);
        Assert.DoesNotContain("\"userId\"", json); Assert.DoesNotContain("\"shopId\"", json);
    }

    [Fact]
    public async Task AnonymousEmptyFeedHasNoCursorAndRequiresNoLogin()
    {
        var ct = TestContext.Current.CancellationToken;
        var bus = DispatchProxy.Create<IMessageBus, ReviewReportApiContractTests.RecordingBus>();
        var recorder = (ReviewReportApiContractTests.RecordingBus)(object)bus;
        recorder.NextResponse = Response<CheckInFeedPageDto>.Success(new([], null));
        var controller = new FeedController(bus, Mock.Of<IUserContext>());
        var response = Assert.IsType<Response<CheckInFeedPageDto>>(
            Assert.IsType<OkObjectResult>(await controller.Get(ct: ct)).Value);
        Assert.Empty(response.Data.Items); Assert.Null(response.Data.NextCursor);
        Assert.Equal(new GetCheckInFeedQuery(null), recorder.LastMessage);
        Assert.NotNull(typeof(FeedController).GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.True(typeof(FeedController).GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
    }
}
