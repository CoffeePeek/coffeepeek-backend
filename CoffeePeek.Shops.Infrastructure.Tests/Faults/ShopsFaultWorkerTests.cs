using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Contract.Events;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Roasters;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using System.Diagnostics.Metrics;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Infrastructure.Tests.Faults;

public sealed class ShopsFaultWorkerTests
{
    [Fact]
    public async Task RunIsolatedWorker()
    {
        Assert.SkipUnless(ShopsFaultControl.IsConfigured && Environment.GetEnvironmentVariable("CP_FAULT_ROLE") != null,
            "Invoked only as an isolated child of the required fault gate.");
        var ct = TestContext.Current.CancellationToken;
        var role = ShopsFaultControl.Required("CP_FAULT_ROLE");
        var root = ShopsFaultControl.Required("CP_FAULT_ROOT");
        var connection = ShopsFaultControl.Required("CP_FAULT_DB");
        Assert.True(LivePostgresServer.IsOwnedDatabaseName(new Npgsql.NpgsqlConnectionStringBuilder(connection).Database));
        using var metrics = new MeterListener();
        metrics.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "CoffeePeek.Shops" && instrument.Name == "shops.cache_invalidation.failures")
                listener.EnableMeasurementEvents(instrument);
        };
        metrics.SetMeasurementEventCallback<long>((_, _, _, _) =>
            File.WriteAllText(ShopsFaultControl.Marker(root, "telemetry-seen"), "failure-recorded"));
        metrics.Start();
        using var host = ShopsFaultHost.Build(role, root, connection);
        await host.StartAsync(ct);
        File.WriteAllText(ShopsFaultControl.Marker(root, role + "-ready"), "ready");
        if (ShopsFaultControl.Required("CP_FAULT_EXECUTE") == "1")
        {
            await ShopsFaultControl.WaitMarkerAsync(root, "execute", ct);
            var bus = host.Services.GetRequiredService<IMessageBus>();
            var id = Guid.Parse(ShopsFaultControl.Required("CP_FAULT_ENTITY"));
            if (role == "redis")
            {
                var response = await bus.InvokeAsync<Response<AdminPublishedShopDto>>(new UpdateAdminCoffeeShopCommand(
                    id, "Committed fault profile", null, CoffeePeek.Contract.Enums.PriceRange.Moderate,
                    null, null, null, null, null), ct);
                Assert.True(response.IsSuccess);
            }
            else if (role == "sender")
            {
                var command = new CreateRoasterCommand("Fault " + id,
                    Photos: [new("client.jpg", "image/jpeg", id.ToString("N"), 1)],
                    ActorUserId: Guid.Parse(ShopsFaultControl.Required("CP_FAULT_ACTOR")));
                if (ShopsFaultControl.Required("CP_FAULT_CHECKPOINT") == "lost-media-reply")
                {
                    await Assert.ThrowsAsync<ServiceUnavailableException>(() => bus.InvokeAsync<Response<RoasterDto>>(command, ct));
                    File.WriteAllText(ShopsFaultControl.Marker(root, "response-lost"), "reply-lost");
                    await ShopsFaultControl.WaitMarkerAsync(root, "retry", ct);
                }
                var response = await bus.InvokeAsync<Response<RoasterDto>>(command, ct);
                Assert.True(response.IsSuccess);
            }
            else if (role == "replay")
            {
                await using var db = ShopsFaultHost.MediaContext(ShopsFaultControl.Required("CP_FAULT_MEDIA_DB"));
                var photo = await db.Photos.FindAsync([id], ct);
                Assert.NotNull(photo);
                await bus.PublishAsync(new ShopPhotosAttachedEvent(photo.OwnerId, photo.ShopAttachmentId!.Value,
                    photo.ShopAttachmentKind!.Value, [photo.StorageKey]));
            }
            else throw new InvalidOperationException("Unexpected fault worker role.");
            File.WriteAllText(ShopsFaultControl.Marker(root, "committed"), "committed");
        }
        await Task.Delay(Timeout.Infinite, ct);
    }
}
