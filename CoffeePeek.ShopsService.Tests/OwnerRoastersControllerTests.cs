using System.Net;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Roasters;
using CoffeePeek.Shops.Application.Features.Catalogs.GetRoasterById;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.ShopsService.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Wolverine;

namespace CoffeePeek.ShopsService.Tests;

public class OwnerRoastersControllerTests
{
    [Theory]
    [InlineData(null, 428)]
    [InlineData("", 400)]
    [InlineData("-1", 400)]
    [InlineData("W/\"1\"", 400)]
    public async Task PutRequiresValidRevisionBeforeDispatch(string? header, int expectedStatus)
    {
        var bus = new Mock<IMessageBus>(MockBehavior.Strict);
        var result = await new OwnerRoastersController(bus.Object, Mock.Of<IUserContext>())
            .UpdateMyRoaster(Guid.NewGuid(), new UpdateRoasterRequest("Roaster"), CancellationToken.None, header);

        Assert.Equal(expectedStatus, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        bus.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(200)]
    [InlineData(404)]
    [InlineData(409)]
    public async Task PutUsesAuthenticatedOwnerAndPreservesOutcome(int status)
    {
        var owner = Guid.NewGuid();
        var roasterId = Guid.NewGuid();
        var user = new Mock<IUserContext>();
        user.Setup(u => u.GetUserIdOrThrow()).Returns(owner);
        var bus = new Mock<IMessageBus>();
        bus.Setup(b => b.InvokeAsync<Response<RoasterDto>>(It.IsAny<UpdateRoasterCommand>(),
                It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(status == 200 ? Response<RoasterDto>.Success(new RoasterDto())
                : Response<RoasterDto>.Error((HttpStatusCode)status, "Failed"));

        var result = await new OwnerRoastersController(bus.Object, user.Object)
            .UpdateMyRoaster(roasterId, new UpdateRoasterRequest("Roaster"), CancellationToken.None, "\"7\"");

        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        bus.Verify(b => b.InvokeAsync<Response<RoasterDto>>(It.Is<UpdateRoasterCommand>(c =>
                c.Id == roasterId && c.OwnerUserId == owner && c.ActorUserId == owner && c.ExpectedRevision == 7),
            It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task ReadsAreScopedToAuthenticatedOwner()
    {
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        var user = new Mock<IUserContext>();
        user.Setup(u => u.GetUserIdOrThrow()).Returns(owner);
        var bus = new Mock<IMessageBus>();
        bus.Setup(b => b.InvokeAsync<Response<RoasterDetailsDto>>(It.IsAny<GetRoasterByIdQuery>(),
                It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<RoasterDetailsDto>.Error(HttpStatusCode.NotFound, "Not found"));
        var controller = new OwnerRoastersController(bus.Object, user.Object);

        await controller.GetMyRoasters(CancellationToken.None, 2, 10);
        await controller.GetMyRoaster(id, CancellationToken.None);

        bus.Verify(b => b.InvokeAsync<Response<OwnerRoastersPage>>(new GetOwnerRoastersQuery(owner, 2, 10),
            It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Once);
        bus.Verify(b => b.InvokeAsync<Response<RoasterDetailsDto>>(new GetRoasterByIdQuery(id, owner),
            It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task AdminPatchAcceptsLegacyRequestWithoutRevision()
    {
        var actor = Guid.NewGuid();
        var user = new Mock<IUserContext>();
        user.Setup(u => u.GetUserIdOrThrow()).Returns(actor);
        var bus = new Mock<IMessageBus>();
        bus.Setup(b => b.InvokeAsync<Response<RoasterDto>>(It.IsAny<UpdateRoasterCommand>(),
                It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<RoasterDto>.Success(new RoasterDto()));

        await new AdminRoastersController(bus.Object, user.Object)
            .Update(Guid.NewGuid(), new UpdateRoasterRequest("Roaster"), CancellationToken.None);

        bus.Verify(b => b.InvokeAsync<Response<RoasterDto>>(It.Is<UpdateRoasterCommand>(c =>
                c.ActorUserId == actor && c.OwnerUserId == null && c.ExpectedRevision == null),
            It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Once);
    }
}
