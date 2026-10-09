using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Shops.Application.Features.AppDownloads;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;
using FluentAssertions;
using Moq;
using Xunit;

#nullable enable

namespace CoffeePeek.Shops.Application.Tests.Features.AppDownloads;

public class AppDownloadRedirectHandlersTests
{
    [Fact]
    public async Task Lookup_DoesNotRecordAnalytics()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new Mock<IAppDistributionRepository>();
        var settings = AppDistributionSettings.CreateDefault();
        settings.UpdateGooglePlay("https://play.google.com/store/apps/details?id=by.coffeepeek.app", true);
        repository.Setup(x => x.GetSettingsOrDefaultAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);

        var result = await GetAppDownloadRedirectHandler.Handle(
            new GetAppDownloadRedirectQuery("android"), repository.Object, ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be("https://play.google.com/store/apps/details?id=by.coffeepeek.app");
        repository.Verify(x => x.AddRedirectEvent(It.IsAny<AppDownloadRedirectEvent>()), Times.Never);
    }

    [Fact]
    public async Task Record_UsesStableEventId_AndSkipsDuplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new Mock<IAppDistributionRepository>();
        var eventId = Guid.NewGuid();
        var command = new RecordAppDownloadRedirectCommand(eventId, "android", "agent", null, null);
        AppDownloadRedirectEvent? saved = null;
        repository.SetupSequence(x => x.HasRedirectEventAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false)
            .ReturnsAsync(true);
        repository.Setup(x => x.AddRedirectEvent(It.IsAny<AppDownloadRedirectEvent>()))
            .Callback<AppDownloadRedirectEvent>(value => saved = value);

        await RecordAppDownloadRedirectHandler.Handle(command, repository.Object, ct);
        await RecordAppDownloadRedirectHandler.Handle(command, repository.Object, ct);

        saved.Should().NotBeNull();
        saved!.Id.Should().Be(eventId);
        repository.Verify(x => x.AddRedirectEvent(It.IsAny<AppDownloadRedirectEvent>()), Times.Once);
    }
}
