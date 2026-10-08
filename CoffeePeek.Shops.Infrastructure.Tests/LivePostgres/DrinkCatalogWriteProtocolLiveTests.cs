using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shared.Persistence.Cache.Redis;
using CoffeePeek.Shops.Application.Features.Catalogs.Drinks;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Testing;
using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class DrinkCatalogWriteProtocolLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;

    public DrinkCatalogWriteProtocolLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task Create_InvalidatesOnlyAfterCatalogCommit()
    {
        var ct = TestContext.Current.CancellationToken;
        var slug = $"test_{Guid.NewGuid():N}";
        var invalidated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new Mock<IStrictCacheInvalidator>();
        cache.Setup(c => c.RemoveAsync(It.IsAny<CacheKey>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await using var observed = fixture.Context();
                invalidated.TrySetResult(await observed.CoffeeDrinkDefinitions.AsNoTracking()
                    .AnyAsync(d => d.Slug == slug, ct));
            });
        cache.Setup(c => c.RemoveByPatternAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddScoped(_ => fixture.Context());
                services.AddScoped<ICoffeeDrinkRepository, QueryCoffeeDrinkRepository>();
                services.AddSingleton(cache.Object);
            })
            .UseWolverine(options =>
            {
                options.ApplicationAssembly = typeof(DrinkCatalogWriteProtocolLiveTests).Assembly;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(CreateCatalogDrinkHandler));
                options.Discovery.IncludeType(typeof(DrinkCatalogCacheInvalidationRequestedHandler));
                options.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
                options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(ShopsDbContext));
                options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(ICoffeeDrinkRepository));
                options.PersistMessagesWithPostgresql(fixture.ConnectionString);
                options.Policies.UseDurableOutboxOnAllSendingEndpoints();
                options.Policies.UseDurableInboxOnAllListeners();
                options.Policies.UseDurableLocalQueues();
                options.Durability.Mode = DurabilityMode.Solo;
                options.UseEntityFrameworkCoreTransactions();
                options.Policies.AutoApplyTransactions();
            })
            .Build();

        await host.StartAsync(ct);
        try
        {
            var bus = host.Services.GetRequiredService<IMessageBus>();
            var response = await bus.InvokeAsync<Response<AdminDrinkDto>>(
                new CreateCatalogDrinkCommand(slug, "Тест", "Test", CoffeeDrinkCategory.Filter,
                    "", 10, true), ct);

            Assert.True(response.IsSuccess);
            Assert.True(await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(20), ct),
                "The cache consumer ran before the drink catalog commit was visible.");
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task UniqueSlugConflict_AtFinalSave_MapsTo409()
    {
        var ct = TestContext.Current.CancellationToken;
        var slug = $"test_{Guid.NewGuid():N}";
        await using (var seed = fixture.Context())
        {
            seed.CoffeeDrinkDefinitions.Add(CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), slug,
                "Первый", "First", CoffeeDrinkCategory.Filter, "", 10));
            await seed.SaveChangesAsync(ct);
        }

        await using var duplicate = fixture.Context();
        duplicate.CoffeeDrinkDefinitions.Add(CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(), slug,
            "Второй", "Second", CoffeeDrinkCategory.Filter, "", 11));

        var error = await Assert.ThrowsAsync<ConflictException>(() => duplicate.SaveChangesAsync(ct));
        Assert.Equal(409, error.StatusCode);
    }
}
