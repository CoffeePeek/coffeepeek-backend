using CoffeePeek.MediaService.Controllers;
using CoffeePeek.MediaService.Services.Backfill;
using CoffeePeek.Shared.Auth.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CoffeePeek.MediaService.Tests;

public sealed class PhotoBackfillRunAuthorizationTests
{
    [Fact]
    public async Task RunManagementRequiresTrustedServiceAuthenticationForEveryOperation()
    {
        var controller = new InternalPhotoBackfillRunsController(null!,
            Options.Create(new GatewayAuthOptions { SecretKey = "backfill-test-service-secret" }))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var id = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        Assert.IsType<UnauthorizedResult>(await controller.Create(new(DateTime.UtcNow, DateTime.UtcNow.AddDays(-1)), ct));
        Assert.IsType<UnauthorizedResult>(await controller.Status(id, ct));
        Assert.IsType<UnauthorizedResult>(await controller.Advance(id, ct));
        Assert.IsType<UnauthorizedResult>(await controller.Pause(id, ct));
        Assert.IsType<UnauthorizedResult>(await controller.Resume(id, ct));
    }
}
