using CoffeePeek.Shared.Persistence.Cache.Redis;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Testing;
using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

[Trait("Category", LivePostgresServer.Category)]
public sealed class CheckInHelpfulOutboxLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;

    public CheckInHelpfulOutboxLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task HelpfulVote_RedisFailureAfterCommit_RetriesWithoutRollingBackVote()
    {
        var ct = TestContext.Current.CancellationToken;
        var city = new City("Helpful-" + Guid.NewGuid().ToString("N"));
        var shop = ShopsLiveDatabase.NewShop("Helpful-shop-" + Guid.NewGuid().ToString("N"), city.Id);
        var ownerId = Guid.NewGuid();
        var voterId = Guid.NewGuid();
        var checkIn = CheckIn.CreateUnified(ownerId, shop.Id, "owner", "good coffee",
            Rating.Create(4, 4, 4), DateTime.UtcNow.AddMinutes(-1), CheckInVisibility.Public);
        Assert.True(checkIn.ApplyModerationDecision(checkIn.ContentRevision, Guid.NewGuid(), 1,
            CheckInModerationState.Approved, null, DateTime.UtcNow));
        await using (var seed = fixture.Context(false))
        {
            seed.Cities.Add(city);
            seed.Shops.Add(shop);
            seed.CheckIns.Add(checkIn);
            await seed.SaveChangesAsync(ct);
        }

        var invalidator = new FailOnceStrictCacheInvalidator(shop.Id, async callbackCt =>
        {
            await using var db = fixture.Context(false);
            return await db.CheckIns.AsNoTracking().AnyAsync(
                       row => row.Id == checkIn.Id && row.HelpfulCount == 1, callbackCt) &&
                   await db.CheckInHelpfulVotes.AsNoTracking().AnyAsync(
                       vote => vote.CheckInId == checkIn.Id && vote.UserId == voterId, callbackCt);
        });
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddScoped(_ => fixture.Context(false));
                services.AddScoped<IQueryCheckInRepository, QueryCheckInRepository>();
                services.AddScoped<ICheckInQueries, CheckInQueries>();
                services.AddSingleton(Moq.Mock.Of<IAuthorVisibilityLookup>());
                services.AddSingleton<IStrictCacheInvalidator>(invalidator);
            })
            .UseWolverine(options =>
            {
                options.ApplicationAssembly = typeof(CheckInHelpfulOutboxLiveTests).Assembly;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(CheckInHelpfulHandler));
                options.Discovery.IncludeType(typeof(CheckInCacheInvalidationRequestedHandler));
                options.UseRuntimeCompilation();
                options.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
                options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(ShopsDbContext));
                options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(IQueryCheckInRepository));
                options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(ICheckInQueries));
                options.PersistMessagesWithPostgresql(fixture.ConnectionString, "wolverine");
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
            var response = await bus.InvokeAsync<Response<CheckInHelpfulResponse>>(
                new SetCheckInHelpfulCommand(checkIn.Id, voterId, true), ct);
            Assert.True(response.IsSuccess);

            Assert.True(await invalidator.FirstAttempt.WaitAsync(TimeSpan.FromSeconds(20), ct),
                "The cache consumer ran before the helpful vote commit was visible.");
            await invalidator.Recovered.WaitAsync(TimeSpan.FromSeconds(30), ct);
            Assert.Equal(2, invalidator.DetailAttempts);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }
}
