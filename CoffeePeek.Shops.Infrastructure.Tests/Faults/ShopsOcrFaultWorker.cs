using CoffeePeek.Contract.Events.Menu;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Modules;
using CoffeePeek.Shops.Persistance.Menu;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using Wolverine;

namespace CoffeePeek.Shops.Infrastructure.Tests.Faults;

public static class ShopsOcrFaultWorker
{
    public static async Task RunAsync(string root, string connection, CancellationToken ct)
    {
        var checkpoint = ShopsFaultControl.Required("CP_FAULT_CHECKPOINT");
        var application = "cp-ocr-" + Path.GetFileName(root);
        var workerConnection = new NpgsqlConnectionStringBuilder(connection) { ApplicationName = application }.ConnectionString;
        using var host = ShopsFaultHost.Build("ocr", root, workerConnection);
        await host.StartAsync(ct);
        var provider = host.Services;
        var media = Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.test", ShopBucketName = "shops" });
        var parser = new Mock<IMenuVisionParser>();
        parser.Setup(p => p.ParseAsync(It.IsAny<IReadOnlyList<MenuVisionPhoto>>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>().UseNpgsql(connection).Options);
                Assert.Equal(0, await db.Database.SqlQueryRaw<int>("""
                    SELECT count(*)::integer AS "Value" FROM pg_stat_activity
                    WHERE application_name = {0} AND state = 'idle in transaction'
                    """, application).SingleAsync(ct));
                var calls = ShopsFaultControl.Marker(root, "provider-calls");
                File.WriteAllText(calls, (File.Exists(calls) ? int.Parse(File.ReadAllText(calls)) + 1 : 1).ToString());
                return new MenuVisionParseResult(true, null, [new("espresso", 8m, .9)]);
            });
        var downloader = new Mock<IMenuPhotoDownloader>();
        downloader.Setup(d => d.DownloadAsync(It.IsAny<IReadOnlyList<MenuPhotoDownloadRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MenuVisionPhoto([1, 2, 3], "image/jpeg")]);
        await using var scope = provider.CreateAsyncScope();
        var handler = new ParseMenuOcrJobRequestedEventHandler(provider.GetRequiredService<IServiceScopeFactory>(), media,
            parser.Object, scope.ServiceProvider.GetRequiredService<IQueryCoffeeDrinkRepository>(), downloader.Object,
            Options.Create(new MenuPriceRangeOptions()), provider.GetRequiredService<ILogger<ParseMenuOcrJobRequestedEventHandler>>());
        File.WriteAllText(ShopsFaultControl.Marker(root, "ocr-ready"), "ready");
        await ShopsFaultControl.WaitMarkerAsync(root, "execute", ct);
        var jobId = Guid.Parse(ShopsFaultControl.Required("CP_FAULT_ENTITY"));
        if (checkpoint == "recover-ocr")
            Assert.Equal(MenuOcrJobStatus.Failed, await scope.ServiceProvider.GetRequiredService<MenuOcrJobStore>()
                .RecoverUnknownOutcomeAsync(jobId, DateTime.UtcNow, ct));
        else await handler.Handle(new ParseMenuOcrJobRequestedEvent(jobId, null), ct);
        File.WriteAllText(ShopsFaultControl.Marker(root, "committed"), "handler-returned");
        await Task.Delay(Timeout.Infinite, ct);
    }
}
