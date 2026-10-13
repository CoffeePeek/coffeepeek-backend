using System.Reflection;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.ShopsService.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Wolverine;

namespace CoffeePeek.ShopsService.Tests;

public class HomeCheckInControllerTests
{
    [Fact]
    public void OnlyDetailAllowsAnonymousAndHomeInputsDoNotAcceptShopScores()
    {
        var type = typeof(HomeCheckInsController);
        Assert.Equal("api/v1/check-ins/home", type.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>());
        Assert.True(type.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        Assert.Equal("Get", Assert.Single(type.GetMethods().Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null)).Name);
        Assert.Contains(type.GetMethods(), m => m.GetCustomAttribute<HttpGetAttribute>()?.Template == "calendar");
        Assert.Contains(type.GetMethods(), m => m.GetCustomAttribute<HttpGetAttribute>()?.Template == "stats/beans");
        Assert.Contains(type.GetMethods(), m => m.GetCustomAttribute<HttpGetAttribute>()?.Template == "stats/methods");

        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "CoffeePeek.Gateway/appsettings.json")));
        var routes = config.RootElement.GetProperty("ReverseProxy").GetProperty("Routes");
        Assert.Contains(routes.EnumerateObject(), route => route.Value.GetProperty("Match").GetProperty("Path")
            .GetString() == "/api/v1/check-ins/{**remainder}"
            && route.Value.GetProperty("ClusterId").GetString() == "shops-cluster");

        foreach (var name in new[] { "CoffeeShopSlug", "CoffeeShopId", "Rating", "PlaceScore", "ServiceScore", "ServingCount" })
            Assert.Null(typeof(CreateHomeCheckInRequest).GetProperty(name));
    }

    [Fact]
    public async Task OwnerIsTakenFromPrincipalForCalendarStatisticsAndDetail()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        var day = new DateOnly(2026, 9, 21);
        var user = new Mock<IUserContext>();
        user.Setup(u => u.GetUserIdOrThrow()).Returns(owner);
        user.Setup(u => u.GetUserId()).Returns(owner);
        var queries = new Mock<ICheckInQueries>();
        queries.Setup(q => q.GetHomeCalendarAsync(owner, day, day, ct))
            .ReturnsAsync([new HomeCalendarDayDto(day, 2)]);
        queries.Setup(q => q.GetHomeBeanStatsAsync(owner, ct))
            .ReturnsAsync([new HomeBeanStatsDto(null, "custom", 2, 2, 36m, 1, 4m)]);
        queries.Setup(q => q.GetHomeMethodStatsAsync(owner, ct))
            .ReturnsAsync([new HomeMethodStatsDto("espresso", 2, 2, 36m, 1, 4m)]);
        queries.Setup(q => q.GetHomeByIdAsync(id, owner, ct)).ReturnsAsync((HomeCheckInDto?)null);
        var controller = new HomeCheckInsController(Mock.Of<IMessageBus>(), user.Object, queries.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        Assert.IsType<BadRequestObjectResult>(await controller.Calendar(null, day, ct));
        queries.VerifyNoOtherCalls();
        Assert.IsType<OkObjectResult>(await controller.Calendar(day, day, ct));
        Assert.IsType<OkObjectResult>(await controller.BeanStats(ct));
        Assert.IsType<OkObjectResult>(await controller.MethodStats(ct));
        Assert.IsType<NotFoundObjectResult>(await controller.Get(id, ct));
        queries.Verify(q => q.GetHomeCalendarAsync(owner, day, day, ct), Times.Once);
        queries.Verify(q => q.GetHomeBeanStatsAsync(owner, ct), Times.Once);
        queries.Verify(q => q.GetHomeMethodStatsAsync(owner, ct), Times.Once);
        queries.Verify(q => q.GetHomeByIdAsync(id, owner, ct), Times.Once);
    }

    [Fact]
    public async Task HistoryUsesInclusiveLocalDateBoundsAndReturnsPaginationHeaders()
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = Guid.NewGuid();
        var day = new DateOnly(2026, 9, 21);
        var user = new Mock<IUserContext>();
        user.Setup(u => u.GetUserIdOrThrow()).Returns(owner);
        user.Setup(u => u.GetUserId()).Returns(owner);
        var queries = new Mock<ICheckInQueries>();
        queries.Setup(q => q.GetHomeByUserAsync(owner, 1, 20, day, day, ct))
            .ReturnsAsync(new HomeCheckInPage([], 2));
        var controller = new HomeCheckInsController(Mock.Of<IMessageBus>(), user.Object, queries.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        Assert.IsType<BadRequestObjectResult>(await controller.Mine(from: day.AddDays(1), to: day, ct: ct));
        Assert.IsType<OkObjectResult>(await controller.Mine(from: day, to: day, ct: ct));
        Assert.Equal("2", controller.Response.Headers["X-Total-Count"].ToString());
        Assert.Equal("1", controller.Response.Headers["X-Total-Pages"].ToString());
        queries.Verify(q => q.GetHomeByUserAsync(owner, 1, 20, day, day, ct), Times.Once);
    }
}
