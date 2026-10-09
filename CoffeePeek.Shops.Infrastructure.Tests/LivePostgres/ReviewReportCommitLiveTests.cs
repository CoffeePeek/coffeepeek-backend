using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Review.Reports;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using CoffeePeek.Shops.Domain.Entities;
using CoffeePeek.Shops.Persistance.Configuration;
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
public sealed class ReviewReportCommitLiveTests : IClassFixture<ShopsLiveDatabase>
{
    private readonly ShopsLiveDatabase fixture;

    public ReviewReportCommitLiveTests(ShopsLiveDatabase fixture)
    {
        Assert.SkipUnless(LivePostgresServer.IsConfigured, LivePostgresServer.SkipReason);
        this.fixture = fixture;
    }

    [Fact]
    public async Task Submit_ReturnsOnlyAfterReportIsCommitted()
    {
        var ct = TestContext.Current.CancellationToken;
        var city = new City("Report-city-" + Guid.NewGuid().ToString("N"));
        var shop = ShopsLiveDatabase.NewShop("Report-shop-" + Guid.NewGuid().ToString("N"), city.Id);
        var checkIn = CheckIn.CreateUnified(Guid.NewGuid(), shop.Id, "author", "good coffee",
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

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddScoped(_ => fixture.Context(false));
                services.AddScoped<IReviewReportRepository, ReviewReportRepository>();
                services.AddScoped<IQueryCheckInRepository, QueryCheckInRepository>();
            })
            .UseWolverine(options =>
            {
                options.ApplicationAssembly = typeof(ReviewReportCommitLiveTests).Assembly;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(SubmitReviewReportHandler));
                options.UseRuntimeCompilation();
                options.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
                options.CodeGeneration.AlwaysUseServiceLocationFor(typeof(ShopsDbContext));
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
            var reporterId = Guid.NewGuid();
            var response = await host.Services.GetRequiredService<IMessageBus>()
                .InvokeAsync<Response<ReviewReportDto>>(
                    new SubmitReviewReportCommand(checkIn.Id, reporterId, "  inaccurate menu  "), ct);
            Assert.True(response.IsSuccess);

            await using var read = fixture.Context(false);
            var committed = await read.ReviewReports.AsNoTracking()
                .SingleAsync(report => report.Id == response.Data.Id, ct);
            Assert.Equal(checkIn.Id, committed.CheckInId);
            Assert.Equal(reporterId, committed.ReportedByUserId);
            Assert.Equal("inaccurate menu", committed.Text);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }
}
