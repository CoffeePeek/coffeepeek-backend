using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.ShopsService.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Wolverine;

namespace CoffeePeek.ShopsService.Tests;

public sealed class CoffeeImportFailureControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_DispatchesOnlyAfterTokenValidation(bool trusted)
    {
        var ct = TestContext.Current.CancellationToken;
        const string token = "coffee-import-test-token-at-least-32-characters";
        var bus = new Mock<IMessageBus>(MockBehavior.Strict);
        var failure = new CoffeeImportFailureDto("source.example", new string('a', 64), DateTimeOffset.UtcNow, "collect", "Failed");
        bus.Setup(b => b.InvokeAsync(It.IsAny<RecordCoffeeImportFailureCommand>(), ct, It.IsAny<TimeSpan?>()))
            .Returns(Task.CompletedTask);
        var controller = new InternalCoffeeImportController(bus.Object, Options.Create(new CoffeeImportAuthOptions { Token = token }))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.Headers[CoffeeImportAuthOptions.Header] = trusted ? token : "untrusted";

        var response = await controller.Failure(failure, ct);

        if (trusted) Assert.IsType<OkObjectResult>(response);
        else Assert.IsType<UnauthorizedResult>(response);
        bus.Verify(b => b.InvokeAsync(It.Is<RecordCoffeeImportFailureCommand>(c => c.Failure == failure), ct,
            It.IsAny<TimeSpan?>()), trusted ? Times.Once() : Times.Never());
    }
}
