using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Application.Features.Owner;
using CoffeePeek.ShopsService.Controllers;
using CoffeePeek.ShopsService.Controllers.Admin;
using CoffeePeek.ShopsService.Controllers.Owner;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Wolverine;

namespace CoffeePeek.ShopsService.Tests;

public sealed class ShopProfileControllerFailureTests
{
    [Theory]
    [InlineData(false, false, 400)]
    [InlineData(false, true, 404)]
    [InlineData(true, false, 400)]
    [InlineData(true, true, 404)]
    public async Task ExpectedTransactionalFailure_PreservesResponseEnvelope(
        bool owner, bool missing, int status)
    {
        var bus = new Mock<IMessageBus>();
        var user = new Mock<IUserContext>();
        var shopId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        user.Setup(u => u.GetUserIdOrThrow()).Returns(ownerId);
        var failure = missing
            ? (Exception)new NotFoundException("Shop not found.")
            : new ValidationException("Invalid profile.");

        IActionResult response;
        if (owner)
        {
            bus.Setup(b => b.InvokeAsync<Response<AdminPublishedShopDto>>(
                    It.IsAny<UpdateOwnerCoffeeShopCommand>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
                .ThrowsAsync(failure);
            response = await new OwnerCoffeeShopsController(bus.Object, user.Object).UpdateMyShop(
                shopId, new UpdateOwnerCoffeeShopRequest("Shop", null, null, null, null, null),
                TestContext.Current.CancellationToken);
        }
        else
        {
            bus.Setup(b => b.InvokeAsync<Response<AdminPublishedShopDto>>(
                    It.IsAny<UpdateAdminCoffeeShopCommand>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
                .ThrowsAsync(failure);
            response = await new AdminCoffeeShopsController(bus.Object, user.Object).UpdateShop(
                shopId, new UpdateAdminCoffeeShopRequest("Shop", null, CoffeePeek.Contract.Enums.PriceRange.Cheap, null),
                TestContext.Current.CancellationToken);
        }

        var result = Assert.IsAssignableFrom<ObjectResult>(response);
        Assert.Equal(status, result.StatusCode);
        var body = Assert.IsType<Response<AdminPublishedShopDto>>(result.Value);
        Assert.False(body.IsSuccess);
        Assert.Equal(failure.Message, body.Message);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ConcurrencyConflict_Returns409ForBothWriters(bool owner, bool duringCommit)
    {
        var bus = new Mock<IMessageBus>();
        var user = new Mock<IUserContext>();
        user.Setup(u => u.GetUserIdOrThrow()).Returns(Guid.NewGuid());
        var failure = duringCommit
            ? (Exception)new DbUpdateConcurrencyException("Concurrent update")
            : new ConflictException("Shop profile changed; reload and retry.", statusCode: 409);
        IActionResult result;

        if (owner)
        {
            bus.Setup(b => b.InvokeAsync<Response<AdminPublishedShopDto>>(
                    It.IsAny<UpdateOwnerCoffeeShopCommand>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
                .ThrowsAsync(failure);
            result = await new OwnerCoffeeShopsController(bus.Object, user.Object).UpdateMyShop(
                Guid.NewGuid(), new UpdateOwnerCoffeeShopRequest("Shop", null, null, null, null, null),
                TestContext.Current.CancellationToken, "\"7\"");
            bus.Verify(b => b.InvokeAsync<Response<AdminPublishedShopDto>>(
                It.Is<UpdateOwnerCoffeeShopCommand>(c => c.ExpectedRevision == 7),
                It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Once);
        }
        else
        {
            bus.Setup(b => b.InvokeAsync<Response<AdminPublishedShopDto>>(
                    It.IsAny<UpdateAdminCoffeeShopCommand>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
                .ThrowsAsync(failure);
            result = await new AdminCoffeeShopsController(bus.Object, user.Object).UpdateShop(
                Guid.NewGuid(), new UpdateAdminCoffeeShopRequest("Shop", null, CoffeePeek.Contract.Enums.PriceRange.Cheap, null),
                TestContext.Current.CancellationToken, "\"7\"");
            bus.Verify(b => b.InvokeAsync<Response<AdminPublishedShopDto>>(
                It.Is<UpdateAdminCoffeeShopCommand>(c => c.ExpectedRevision == 7),
                It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Once);
        }

        var body = Assert.IsType<Response<AdminPublishedShopDto>>(Assert.IsType<ObjectResult>(result).Value);
        Assert.Equal(409, body.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidIfMatch_Returns400BeforeDispatch(bool owner)
    {
        var bus = new Mock<IMessageBus>(MockBehavior.Strict);
        var user = new Mock<IUserContext>();
        IActionResult result = owner
            ? await new OwnerCoffeeShopsController(bus.Object, user.Object).UpdateMyShop(
                Guid.NewGuid(), new UpdateOwnerCoffeeShopRequest("Shop", null, null, null, null, null),
                TestContext.Current.CancellationToken, "W/\"7\"")
            : await new AdminCoffeeShopsController(bus.Object, user.Object).UpdateShop(
                Guid.NewGuid(), new UpdateAdminCoffeeShopRequest("Shop", null, CoffeePeek.Contract.Enums.PriceRange.Cheap, null),
                TestContext.Current.CancellationToken, "W/\"7\"");

        var body = Assert.IsType<Response<AdminPublishedShopDto>>(Assert.IsType<BadRequestObjectResult>(result).Value);
        Assert.Equal(400, body.StatusCode);
        bus.VerifyNoOtherCalls();
    }
}
