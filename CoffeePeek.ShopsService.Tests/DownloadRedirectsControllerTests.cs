using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.AppDownloads;
using CoffeePeek.ShopsService.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wolverine;

namespace CoffeePeek.ShopsService.Tests;

public class DownloadRedirectsControllerTests
{
    [Fact]
    public async Task AnalyticsQueueFailure_RecordsSameEventInline_AndKeepsRedirect()
    {
        var bus = new Mock<IMessageBus>();
        bus.Setup(x => x.InvokeAsync<Response<string>>(
                It.IsAny<GetAppDownloadRedirectQuery>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<string>.Success("https://example.com/app"));
        bus.Setup(x => x.PublishAsync(It.IsAny<RecordAppDownloadRedirectCommand>()))
            .ThrowsAsync(new InvalidOperationException("Queue unavailable"));
        bus.Setup(x => x.InvokeAsync(
                It.IsAny<RecordAppDownloadRedirectCommand>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<TimeSpan?>()))
            .Returns(Task.CompletedTask);
        var controller = new DownloadRedirectsController(bus.Object, NullLogger<DownloadRedirectsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Android(TestContext.Current.CancellationToken);

        Assert.Equal("https://example.com/app", Assert.IsType<RedirectResult>(result).Url);
        bus.Verify(x => x.PublishAsync(It.IsAny<RecordAppDownloadRedirectCommand>()), Times.Once);
        bus.Verify(x => x.InvokeAsync(
            It.IsAny<RecordAppDownloadRedirectCommand>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<TimeSpan?>()), Times.Once);
        var published = Assert.IsType<RecordAppDownloadRedirectCommand>(bus.Invocations
            .Single(i => i.Method.Name == nameof(IMessageBus.PublishAsync)).Arguments[0]);
        var persisted = Assert.IsType<RecordAppDownloadRedirectCommand>(bus.Invocations
            .Single(i => i.Method.Name == nameof(IMessageBus.InvokeAsync) &&
                         i.Arguments[0] is RecordAppDownloadRedirectCommand).Arguments[0]);
        Assert.Same(published, persisted);
    }

    [Fact]
    public async Task AnalyticsQueueAndDirectWriteFailure_StillKeepsResolvedRedirect()
    {
        var bus = new Mock<IMessageBus>();
        bus.Setup(x => x.InvokeAsync<Response<string>>(
                It.IsAny<GetAppDownloadRedirectQuery>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<TimeSpan?>()))
            .ReturnsAsync(Response<string>.Success("https://example.com/app"));
        bus.Setup(x => x.PublishAsync(It.IsAny<RecordAppDownloadRedirectCommand>()))
            .ThrowsAsync(new InvalidOperationException("Queue unavailable"));
        bus.Setup(x => x.InvokeAsync(
                It.IsAny<RecordAppDownloadRedirectCommand>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<TimeSpan?>()))
            .ThrowsAsync(new InvalidOperationException("Database unavailable"));
        var controller = new DownloadRedirectsController(bus.Object, NullLogger<DownloadRedirectsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Ios(TestContext.Current.CancellationToken);

        Assert.Equal("https://example.com/app", Assert.IsType<RedirectResult>(result).Url);
        bus.Verify(x => x.InvokeAsync(
            It.IsAny<RecordAppDownloadRedirectCommand>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<TimeSpan?>()), Times.Once);
    }
}
