using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Application.Observability;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Menu;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.Shops.Infrastructure.Tests.Consumers;

public sealed class ParseMenuOcrJobRequestedEventHandlerTests
{
    [Fact]
    public async Task RedeliveryOfRunningJob_DoesNotPayForVisionAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        using var services = Services();
        var jobId = await SeedJobAsync(services, ct);
        await using (var scope = services.CreateAsyncScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<MenuOcrJobStore>()
                .TryStartCurrentAsync(jobId, ct));
        var parser = Parser();

        await Handler(services, parser.Object)
            .Handle(new ParseMenuOcrJobRequestedEvent(jobId, Guid.NewGuid()), ct);

        parser.Verify(p => p.ParseAsync(It.IsAny<IReadOnlyList<MenuVisionPhoto>>(), ct), Times.Never);
        await using var verifyScope = services.CreateAsyncScope();
        Assert.Equal(MenuOcrJobStatus.Running,
            (await verifyScope.ServiceProvider.GetRequiredService<ShopsDbContext>()
                .MenuOcrJobs.SingleAsync(j => j.Id == jobId, ct)).Status);
    }

    [Fact]
    public async Task OperatorRecovery_ClosesUnknownOutcomeWithoutAnotherProviderCall()
    {
        var ct = TestContext.Current.CancellationToken;
        using var services = Services();
        var jobId = await SeedJobAsync(services, ct);
        await using (var scope = services.CreateAsyncScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<MenuOcrJobStore>()
                .TryStartCurrentAsync(jobId, ct));
        var parser = Parser();
        await using (var scope = services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<MenuOcrJobStore>();
            var tooEarly = await Assert.ThrowsAsync<CoffeePeek.Shared.Kernel.Exceptions.ConflictException>(() =>
                store.RecoverUnknownOutcomeAsync(jobId, DateTime.UtcNow, ct));
            Assert.Equal(409, tooEarly.StatusCode);
        }

        await using (var scope = services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<MenuOcrJobStore>();
            Assert.Equal(MenuOcrJobStatus.Failed, await store.RecoverUnknownOutcomeAsync(
                jobId, DateTime.UtcNow.Add(MenuOcrJobStore.OperatorRecoveryMinimumAge).AddMinutes(1), ct));
        }
        await using (var scope = services.CreateAsyncScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<MenuOcrJobStore>()
                .StoreResultAsync(jobId, "{\"items\":[]}", ct));

        await Handler(services, parser.Object)
            .Handle(new ParseMenuOcrJobRequestedEvent(jobId, Guid.NewGuid()), ct);
        parser.Verify(p => p.ParseAsync(It.IsAny<IReadOnlyList<MenuVisionPhoto>>(), ct), Times.Never);
        await using var verify = services.CreateAsyncScope();
        var job = await verify.ServiceProvider.GetRequiredService<ShopsDbContext>()
            .MenuOcrJobs.SingleAsync(j => j.Id == jobId, ct);
        Assert.Equal(MenuOcrJobStatus.Failed, job.Status);
        Assert.Equal(MenuOcrJob.ProviderOutcomeUnknownError, job.Error);
        var menu = await verify.ServiceProvider.GetRequiredService<ShopsDbContext>()
            .ShopMenus.SingleAsync(ct);
        Assert.Equal(MenuParseStatus.Failed, menu.ParseStatus);
        Assert.Equal(MenuOcrJob.ProviderOutcomeUnknownError, menu.ParseError);
    }

    [Fact]
    public async Task OperatorRecovery_StalesJobAfterManualMenuEdit()
    {
        var ct = TestContext.Current.CancellationToken;
        using var services = Services();
        var jobId = await SeedJobAsync(services, ct);
        await using (var scope = services.CreateAsyncScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<MenuOcrJobStore>()
                .TryStartCurrentAsync(jobId, ct));
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShopsDbContext>();
            var menu = await db.ShopMenus.SingleAsync(ct);
            menu.MarkManualUpdate(Guid.NewGuid());
            await db.SaveChangesAsync(ct);
        }

        await using var recoveryScope = services.CreateAsyncScope();
        var recovered = await recoveryScope.ServiceProvider.GetRequiredService<MenuOcrJobStore>()
            .RecoverUnknownOutcomeAsync(jobId,
                DateTime.UtcNow.Add(MenuOcrJobStore.OperatorRecoveryMinimumAge).AddMinutes(1), ct);
        Assert.Equal(MenuOcrJobStatus.Stale, recovered);
    }

    [Fact]
    public async Task ApplyRetry_UsesSavedResultWithoutCallingVisionAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        using var services = Services();
        var jobId = await SeedJobAsync(services, ct);
        var parser = Parser();
        var apply = services.GetRequiredService<Mock<IApplyShopMenuService>>();
        var applyCalls = 0;
        apply.Setup(a => a.ApplyParseResultAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyList<ParsedMenuItemDto>>(),
                It.IsAny<IReadOnlyList<UnmatchedMenuItemDto>>(),
                It.IsAny<CoffeePeek.Contract.Enums.PriceRange?>(),
                It.IsAny<IReadOnlyList<ShopMenuPhotoSnapshot>>(), It.IsAny<DateTime>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns(() => ++applyCalls == 1
                ? Task.FromException(new InvalidOperationException("apply commit failed"))
                : Task.CompletedTask);
        var handler = Handler(services, parser.Object);
        var message = new ParseMenuOcrJobRequestedEvent(jobId, Guid.NewGuid());

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(message, ct));
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShopsDbContext>();
            var stored = await db.MenuOcrJobs.SingleAsync(j => j.Id == jobId, ct);
            Assert.Equal(MenuOcrJobStatus.ResultStored, stored.Status);
            Assert.NotNull(stored.ResultJson);
        }

        await handler.Handle(message, ct);

        parser.Verify(p => p.ParseAsync(It.IsAny<IReadOnlyList<MenuVisionPhoto>>(), ct), Times.Once);
        Assert.Equal(2, applyCalls);
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShopsDbContext>();
            Assert.Equal(MenuOcrJobStatus.Applied,
                (await db.MenuOcrJobs.SingleAsync(j => j.Id == jobId, ct)).Status);
        }
    }

    [Fact]
    public async Task ManualEditDuringVision_StalesWholeResultBeforeApply()
    {
        var outcomes = new ConcurrentQueue<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, subscriber) =>
        {
            if (instrument.Meter.Name == ShopsTelemetry.InstrumentationName)
                subscriber.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            if (instrument.Name == "shops.ocr.jobs.completed")
            {
                foreach (var tag in tags)
                    if (tag.Key == "outcome" && tag.Value is string outcome)
                        outcomes.Enqueue(outcome);
            }
        });
        listener.Start();

        var ct = TestContext.Current.CancellationToken;
        using var services = Services();
        var jobId = await SeedJobAsync(services, ct);
        var parser = Parser();
        parser.Setup(p => p.ParseAsync(It.IsAny<IReadOnlyList<MenuVisionPhoto>>(), ct))
            .Returns(async () =>
            {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ShopsDbContext>();
                var menu = await db.ShopMenus.SingleAsync(ct);
                menu.ApplyManualItem(Guid.NewGuid(), MenuItemAvailability.Present, 5m, Guid.NewGuid());
                await db.SaveChangesAsync(ct);
                return new MenuVisionParseResult(true, null,
                    [new VisionDrinkLine("Cappuccino", 8m, 0.9)]);
            });
        var apply = services.GetRequiredService<Mock<IApplyShopMenuService>>();

        await Handler(services, parser.Object)
            .Handle(new ParseMenuOcrJobRequestedEvent(jobId, Guid.NewGuid()), ct);

        apply.Verify(a => a.ApplyParseResultAsync(
            It.IsAny<Guid>(), It.IsAny<IReadOnlyList<ParsedMenuItemDto>>(),
            It.IsAny<IReadOnlyList<UnmatchedMenuItemDto>>(),
            It.IsAny<CoffeePeek.Contract.Enums.PriceRange?>(),
            It.IsAny<IReadOnlyList<ShopMenuPhotoSnapshot>>(), It.IsAny<DateTime>(),
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        await using var verifyScope = services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ShopsDbContext>();
        Assert.Equal(MenuOcrJobStatus.Stale,
            (await verifyDb.MenuOcrJobs.SingleAsync(j => j.Id == jobId, ct)).Status);
        Assert.Contains("stale", outcomes);
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<ShopsDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        services.AddScoped<MenuOcrJobStore>();
        var apply = new Mock<IApplyShopMenuService>();
        services.AddSingleton(apply);
        services.AddSingleton(apply.Object);
        return services.BuildServiceProvider();
    }

    private static async Task<Guid> SeedJobAsync(ServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopsDbContext>();
        var menu = ShopMenu.Create(Guid.NewGuid());
        var photo = ShopMenuPhoto.Create("menu.jpg", "image/jpeg", "menus/one", 100);
        menu.AddPhotos([photo]);
        var job = menu.QueueOcrJob([photo], DateTime.UtcNow);
        db.ShopMenus.Add(menu);
        await db.SaveChangesAsync(ct);
        return job.Id;
    }

    private static Mock<IMenuVisionParser> Parser()
    {
        var parser = new Mock<IMenuVisionParser>();
        parser.Setup(p => p.ParseAsync(It.IsAny<IReadOnlyList<MenuVisionPhoto>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MenuVisionParseResult(true, null,
                [new VisionDrinkLine("Cappuccino", 8m, 0.9)]));
        return parser;
    }

    private static ParseMenuOcrJobRequestedEventHandler Handler(
        ServiceProvider services, IMenuVisionParser parser)
    {
        var drinks = new Mock<IQueryCoffeeDrinkRepository>();
        drinks.Setup(d => d.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync([
            CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), "cappuccino", "Капучино", "Cappuccino",
                CoffeeDrinkCategory.Espresso, "cappuccino", 1)
        ]);
        var downloader = new Mock<IMenuPhotoDownloader>();
        downloader.Setup(d => d.DownloadAsync(
                It.IsAny<IReadOnlyList<MenuPhotoDownloadRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MenuVisionPhoto([1, 2, 3], "image/jpeg")]);
        return new ParseMenuOcrJobRequestedEventHandler(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com" }),
            parser, drinks.Object, downloader.Object,
            Options.Create(new MenuPriceRangeOptions()),
            NullLogger<ParseMenuOcrJobRequestedEventHandler>.Instance);
    }
}
