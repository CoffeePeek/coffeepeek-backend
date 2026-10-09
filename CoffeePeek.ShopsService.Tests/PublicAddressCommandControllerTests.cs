using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.ShopsService.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Wolverine;

namespace CoffeePeek.ShopsService.Tests;

public sealed class PublicAddressCommandControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Change_DispatchesKindOwnerRequestAndAuthenticatedActor(bool rename)
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid(); var actorId = Guid.NewGuid();
        var request = new ChangePublicAddressRequest("new-roaster", rename ? 1 : 0, "Rename");
        var expected = new PublicAddressMetadata(id, request.Slug!, "/roasters/new-roaster", rename ? 2 : 1);
        var bus = new Mock<IMessageBus>(MockBehavior.Strict);
        bus.Setup(b => b.InvokeAsync<PublicAddressMetadata>(It.Is<ChangePublicAddressCommand>(c =>
            c.Kind == PublicAddressKind.Roaster && c.Id == id && c.Request == request && c.ActorId == actorId), ct, It.IsAny<TimeSpan?>()))
            .ReturnsAsync(expected);
        var user = new Mock<IUserContext>(); user.Setup(u => u.GetUserIdOrThrow()).Returns(actorId);
        var controller = new AdminPublicAddressesController(bus.Object, user.Object);

        var response = rename ? await controller.Rename("roasters", id, request, ct) : await controller.Initialize("roasters", id, request, ct);

        Assert.Same(expected, Assert.IsType<OkObjectResult>(response.Result).Value);
        bus.VerifyAll();
    }

    [Fact]
    public async Task Backfill_PreservesCursorLimitDryRunAndAuthenticatedActor()
    {
        var ct = TestContext.Current.CancellationToken;
        var cursor = Guid.NewGuid(); var actorId = Guid.NewGuid();
        var expected = new PublicAddressBackfillResult(true, [], null);
        var bus = new Mock<IMessageBus>(MockBehavior.Strict);
        bus.Setup(b => b.InvokeAsync<PublicAddressBackfillResult>(It.Is<BackfillPublicAddressesCommand>(c =>
            c.Kind == PublicAddressKind.City && c.AfterId == cursor && c.Limit == 25 && c.DryRun && c.ActorId == actorId), ct, It.IsAny<TimeSpan?>()))
            .ReturnsAsync(expected);
        var user = new Mock<IUserContext>(); user.Setup(u => u.GetUserIdOrThrow()).Returns(actorId);

        var response = await new AdminPublicAddressesController(bus.Object, user.Object).Backfill("cities", cursor, 25, true, ct);

        Assert.Same(expected, Assert.IsType<OkObjectResult>(response.Result).Value);
        bus.VerifyAll();
    }

    [Theory]
    [InlineData(false, 1, "slug")]
    [InlineData(true, 0, "slug")]
    [InlineData(true, 1, null)]
    public async Task InvalidChange_DoesNotDispatch(bool rename, long revision, string? slug)
    {
        var ct = TestContext.Current.CancellationToken;
        var bus = new Mock<IMessageBus>(MockBehavior.Strict);
        var controller = new AdminPublicAddressesController(bus.Object, Mock.Of<IUserContext>());
        var request = new ChangePublicAddressRequest(slug, revision, "Reason");
        var error = await Assert.ThrowsAsync<PublicAddressException>(async () =>
        {
            if (rename) await controller.Rename("roasters", Guid.NewGuid(), request, ct);
            else await controller.Initialize("roasters", Guid.NewGuid(), request, ct);
        });
        Assert.Equal(400, error.StatusCode);
        bus.VerifyNoOtherCalls();
    }
}
