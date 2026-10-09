using CoffeePeek.Contract.Events;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Consumers;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Roasters;
using CoffeePeek.Shops.Application.Features.Admin.Shops;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Modules;
using CoffeePeek.Shops.Application.Services;
using CoffeePeek.Shops.Application.Features.CoffeeShop.ShopProfile;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using JasperFx;
using JasperFx.CodeGeneration;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace CoffeePeek.Shops.Infrastructure.Tests.Faults;

public static class ShopsFaultHost
{
    public static MediaDbContext MediaContext(string connection) => new(
        new DbContextOptionsBuilder<MediaDbContext>().UseNpgsql(connection).Options);

    public static MinIOOptions StorageOptions(string bucket) => new()
    {
        Endpoint = ShopsFaultControl.Required("COFFEEPEEK_LIVE_MINIO"),
        AccessKey = "coffee-legacy-test", SecretKey = "coffee-legacy-test-only",
        ShopBucketName = bucket, CheckInBucketName = bucket, UserBucketName = bucket
    };

    public static MinIOStorageService Storage(string bucket) => new(Options.Create(StorageOptions(bucket)),
        Options.Create(new MediaPublicUrlOptions()));

    public static IHost Build(string role, string root, string connection) => Host.CreateDefaultBuilder()
        .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Information))
        .ConfigureServices(services =>
        {
            var checkpoint = ShopsFaultControl.Required("CP_FAULT_CHECKPOINT");
            if (role == "media")
            {
                services.AddScoped(_ => new MediaDbContext(new DbContextOptionsBuilder<MediaDbContext>()
                    .UseNpgsql(connection).AddInterceptors(new FaultMediaCommitInterceptor(root, checkpoint)).Options));
                services.AddScoped<IPhotoRepository, PhotoRepository>();
                services.AddScoped<IUnitOfWork, UnitOfWork<MediaDbContext>>();
                services.AddSingleton<IStorageService>(_ => Storage(ShopsFaultControl.Required("CP_FAULT_BUCKET")));
            }
            else
            {
                services.AddScoped(_ => new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
                    .UseNpgsql(connection).AddInterceptors(new AuditInterceptor(),
                        new FaultOcrBeforeStoreInterceptor(root, checkpoint), new FaultOcrStoredInterceptor(root, checkpoint),
                        new FaultOcrApplyFlushInterceptor(root, checkpoint)).Options));
                services.AddShopsModule().AddCoffeeCatalogModule();
                services.AddSingleton(Options.Create(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.test" }));
                var mapper = new Mock<IMapper>();
                mapper.Setup(m => m.Map<CoffeePeek.Contract.Dtos.Shop.RoasterDto>(It.IsAny<CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.Roaster>()))
                    .Returns((CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.Roaster r) => new() { Id = r.Id, Name = r.Name });
                services.AddSingleton(mapper.Object);
                if (role == "ocr")
                {
                    services.AddMenuModule();
                    services.AddScoped<IShopPriceRangeWriter, ShopPriceRangeWriter>();
                    var cache = new Mock<IStrictCacheInvalidator>();
                    cache.Setup(c => c.RemoveAsync(It.IsAny<CacheKey>(), It.IsAny<CancellationToken>()))
                        .Returns(async () =>
                        {
                            await using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>().UseNpgsql(connection).Options);
                            var jobId = Guid.Parse(ShopsFaultControl.Required("CP_FAULT_ENTITY"));
                            var job = await db.MenuOcrJobs.SingleAsync(j => j.Id == jobId);
                            Assert.True(job.Status is MenuOcrJobStatus.Applied or MenuOcrJobStatus.Failed);
                            File.WriteAllText(ShopsFaultControl.Marker(root, "ocr-invalidated"), "committed-state-observed");
                        });
                    services.AddSingleton(cache.Object);
                }
                else if (role == "redis")
                {
                    services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(
                        "127.0.0.1:26379,abortConnect=false,connectTimeout=1000,asyncTimeout=1000,syncTimeout=1000"));
                    services.AddSingleton<RedisService>();
                    services.AddSingleton<IStrictCacheInvalidator>(provider => new FaultRedisInvalidator(
                        provider.GetRequiredService<RedisService>(), root, checkpoint));
                }
                else services.AddScoped<IUploadedPhotoLookup, FaultPhotoLookup>();
            }
        })
        .UseWolverine(options =>
        {
            options.ApplicationAssembly = typeof(ShopsFaultHost).Assembly;
            options.Discovery.DisableConventionalDiscovery();
            if (role is "redis" or "ocr")
            {
                options.Discovery.IncludeType(typeof(UpdateAdminCoffeeShopHandler));
                options.Discovery.IncludeType(typeof(ShopProfileCacheInvalidationRequestedHandler));
            }
            else
            {
                options.UseRabbitMq(new Uri("amqp://coffee-fault:coffee-fault-test-only@127.0.0.1:25672")).AutoProvision();
                var queue = "cp-fault-" + new Npgsql.NpgsqlConnectionStringBuilder(
                    role == "media" ? connection : ShopsFaultControl.Required("CP_FAULT_MEDIA_DB")).Database;
                if (role == "media")
                {
                    options.Discovery.IncludeType(typeof(ShopPhotosAttachedHandler));
                    options.Policies.AddMiddleware<FaultMediaDeliveryMiddleware>(chain => chain.MessageType == typeof(ShopPhotosAttachedEvent));
                    options.ListenToRabbitQueue(queue).UseDurableInbox();
                }
                else
                {
                    options.Discovery.IncludeType(typeof(CreateRoasterHandler));
                    options.PublishMessage<ShopPhotosAttachedEvent>().ToRabbitQueue(queue).UseDurableOutbox();
                }
            }
            options.UseRuntimeCompilation();
            options.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
            options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(ShopsDbContext));
            options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(MediaDbContext));
            options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(IUploadedPhotoLookup));
            options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(IStrictCacheInvalidator));
            options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(IStorageService));
            options.PersistMessagesWithPostgresql(connection, "wolverine");
            options.Policies.UseDurableOutboxOnAllSendingEndpoints();
            options.Policies.UseDurableInboxOnAllListeners();
            options.Policies.UseDurableLocalQueues();
            options.Durability.Mode = DurabilityMode.Solo;
            options.UseEntityFrameworkCoreTransactions();
            options.Policies.AutoApplyTransactions();
        }).Build();
}
