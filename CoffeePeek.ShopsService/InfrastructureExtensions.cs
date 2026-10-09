using CoffeePeek.Shops.Application;
using CoffeePeek.Shops.Infrastructure;
using CoffeePeek.Shops.Persistance;
using CoffeePeek.Shared.Persistence.Extensions;
using CoffeePeek.Shared.Web.Logging;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Application.Observability;
using CoffeePeek.ShopsService.Health;
using CoffePeek.ServiceDefaults;
using Serilog;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace CoffeePeek.ShopsService;

public static class InfrastructureExtensions
{
    public static WebApplicationBuilder AddApplication(this WebApplicationBuilder builder)
    {
        builder.AddSerilogLogging();
        builder.AddServiceDefaults();
        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddMeter(ShopsTelemetry.InstrumentationName))
            .WithTracing(tracing => tracing.AddSource(ShopsTelemetry.InstrumentationName));
        builder.Services.AddHealthChecks()
            .AddCheck<ShopsDatabaseReadinessCheck>("shops_postgres", tags: ["ready"]);
        builder.WebHost
            .ConfigureWebhost();

        var applicationAssembly = typeof(GetCoffeeShopHandler).Assembly;
        var infrastructureAssembly = typeof(ModerationShopApproveHandler).Assembly;
        builder.AddWolverine(
            typeof(InfrastructureExtensions).Assembly,
            [applicationAssembly, infrastructureAssembly],
            [typeof(ShopsDbContext), typeof(CoffeePeek.Shared.Kernel.Drinks.IDrinkCatalog), typeof(CoffeePeek.Shops.Domain.Aggregates.MenuAggregate.ICoffeeDrinkRepository), typeof(CoffeePeek.Shops.Application.Features.CoffeeCatalog.ICoffeeCatalogStore),
                typeof(CoffeePeek.Shops.Application.Features.CoffeeCatalog.ICoffeeProductWriter),
                typeof(CoffeePeek.Shops.Application.Features.CoffeeCatalog.ICoffeeImportWriter),
                typeof(CoffeePeek.Shops.Application.Features.Discovery.IDiscoveryFavoritesStore),
                typeof(CoffeePeek.Shops.Application.Features.Discovery.IDiscoveryClassificationStore),
                typeof(CoffeePeek.Shops.Application.Features.Feed.ICheckInFeedReader),
                typeof(CoffeePeek.Shops.Application.PublicAddresses.IPublicCheckInProjector),
                typeof(CoffeePeek.Shops.Application.PublicAddresses.IPublicAddressService),
                typeof(CoffeePeek.Shops.Application.Abstractions.IProfileAddressLookup)]);
        
        builder.Services
            .AddApplication()
            .AddPersistence(builder)
            .AddInfrastructure(builder.Configuration)
            .AddPresentation(builder.Configuration);

        return builder;
    }

    public static void UseApplication(this WebApplication app)
    {
        app.UseCoffeePeekRequestLogging();

        app.UseExceptionHandler();
        app.UseResponseCaching();

        app.MapOpenApi();

        app.MapDefaultEndpoints();

        app.MapControllers();
    }
}
