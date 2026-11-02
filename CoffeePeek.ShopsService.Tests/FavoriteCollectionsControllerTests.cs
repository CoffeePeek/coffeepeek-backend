using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shops.Application.Features.Discovery;
using CoffeePeek.ShopsService.Controllers;
using CoffeePeek.ShopsService.Discovery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Moq;
using System.Reflection;
using Wolverine;

namespace CoffeePeek.ShopsService.Tests;

public sealed class FavoriteCollectionsControllerTests
{
    [Fact]
    public void OnlySingleCollectionReadsAreAnonymous_AndDiscoveryErrorsKeepTheirContract()
    {
        var type = typeof(FavoriteCollectionsController);
        Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>());
        Assert.NotNull(type.GetMethod(nameof(FavoriteCollectionsController.Get))!.GetCustomAttribute<AllowAnonymousAttribute>());
        foreach (var name in new[] { nameof(FavoriteCollectionsController.Create), nameof(FavoriteCollectionsController.Mine), nameof(FavoriteCollectionsController.Revoke) })
            Assert.Null(type.GetMethod(name)!.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.True(DiscoveryExceptionFilter.IsDiscoveryAction(new ControllerActionDescriptor { ControllerTypeInfo = type.GetTypeInfo() }));
        Assert.True(type.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
    }

    [Fact]
    public async Task AnonymousGetDoesNotReadViewerIdentity_AndUnavailableLinkIs404()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var store = new Mock<IFavoriteCollectionsStore>(MockBehavior.Strict);
        store.Setup(s => s.GetAsync(id, ct)).ReturnsAsync((FavoriteCollectionDto?)null);
        var controller = new FavoriteCollectionsController(store.Object, new Mock<IUserContext>(MockBehavior.Strict).Object,
            new Mock<IMessageBus>(MockBehavior.Strict).Object);
        Assert.IsType<NotFoundResult>(await controller.Get(id, ct));
        store.VerifyAll();
    }
}
