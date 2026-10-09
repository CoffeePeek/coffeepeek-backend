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
        var services = new ServiceCollection().AddLogging();
        services.AddScoped(_ => new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>().UseNpgsql(workerConnection)
            .AddInterceptors(new AuditInterceptor(), new FaultOcrBeforeStoreInterceptor(root, checkpoint),
                new FaultOcrStoredInterceptor(root, checkpoint)).Options));
        services.AddShopsModule().AddMenuModule();
        services.AddScoped<IApplyShopMenuService, ApplyShopMenuService>();
        services.AddScoped<IShopPriceRangeWriter, ShopPriceRangeWriter>();
        var bus = new Mock<IMessageBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<object>())).Returns(ValueTask.CompletedTask);
        services.AddSingleton(bus.Object);
        var media = Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.test", ShopBucketName = "shops" });
        services.AddSingleton(media);
        await using var provider = services.BuildServiceProvider();
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
                return new MenuVisionParseResult(true, null, [new("espresso", 8m, 200, .9)]);
            });
        var downloader = new Mock<IMenuPhotoDownloader>();
        downloader.Setup(d => d.DownloadAsync(It.IsAny<IReadOnlyList<MenuPhotoDownloadRequest>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MenuVisionPhoto([1, 2, 3], "image/jpeg")]);
        await using var scope = provider.CreateAsyncScope();
        var handler = new ParseMenuOcrJobRequestedEventHandler(provider.GetRequiredService<IServiceScopeFactory>(), media,
            parser.Object, scope.ServiceProvider.GetRequiredService<IQueryCoffeeDrinkRepository>(), downloader.Object,
            Options.Create(new MenuPriceRangeOptions()), NullLogger<ParseMenuOcrJobRequestedEventHandler>.Instance);
        File.WriteAllText(ShopsFaultControl.Marker(root, "ocr-ready"), "ready");
        await ShopsFaultControl.WaitMarkerAsync(root, "execute", ct);
        await handler.Handle(new ParseMenuOcrJobRequestedEvent(Guid.Parse(ShopsFaultControl.Required("CP_FAULT_ENTITY")), null), ct);
        File.WriteAllText(ShopsFaultControl.Marker(root, "committed"), "handler-returned");
        await Task.Delay(Timeout.Infinite, ct);
    }
}
