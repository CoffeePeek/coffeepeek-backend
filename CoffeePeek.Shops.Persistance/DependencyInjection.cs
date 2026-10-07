using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Extentions;
using CoffeePeek.Shared.Persistence;
using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Shared.Persistence.Extensions;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Application.Features.CoffeeZones;
using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Queries;
using CoffeePeek.Shops.Persistance.Repositories;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Persistance.PublicAddresses;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CoffeePeek.Shops.Persistance;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, WebApplicationBuilder builder)
    {
        services.AddScoped<CoffeePeek.Shops.Application.Features.Discovery.IDiscoveryQueryStore, CoffeePeek.Shops.Persistance.Discovery.DiscoveryQueryStore>();
        services.AddScoped<CoffeePeek.Shops.Application.Features.Discovery.IDiscoveryClassificationStore, CoffeePeek.Shops.Persistance.CoffeeCatalog.DiscoveryClassificationStore>();
        services.AddScoped<CoffeePeek.Shops.Application.Features.Discovery.IDiscoveryFavoritesStore, CoffeePeek.Shops.Persistance.Discovery.DiscoveryFavoritesStore>();
        services.Configure<PublicAddressOptions>(builder.Configuration.GetSection("PublicAddresses"));
        services.Configure<CoffeePeek.Shops.Application.Features.CoffeeCatalog.CoffeeImportOptions>(builder.Configuration.GetSection("CoffeeImportOptions"));
        services.AddScoped<CoffeePeek.Shops.Application.Features.CoffeeCatalog.ICoffeeCatalogStore, CoffeePeek.Shops.Persistance.CoffeeCatalog.CoffeeCatalogStore>();
        services.AddScoped<IPublicAddressService, PublicAddressService>();
        if (builder.Environment.IsDevelopment())
        {
            builder.AddNpgsqlDbContext<ShopsDbContext>(
                connectionName: AppResources.ShopsDb,
                configureDbContextOptions: opt => opt.AddInterceptors(new CheckInAuditInterceptor()),
                configureSettings: settings => { settings.DisableRetry = true; }
            );
        }
        else
        {
            var connectionString = services.AddValidateOptions<PostgresCpOptions>().ConnectionString;

            services.AddDatabase<ShopsDbContext>(
                connectionString,
                opt => opt.AddInterceptors(new CheckInAuditInterceptor())
            );
        }

        services.AddScoped<IUnitOfWork, UnitOfWork<ShopsDbContext>>();


        // Queries
        services.AddScoped<ICheckInQueries, CheckInQueries>();
        services.AddScoped<ICoffeeShopQueries, CoffeeShopQueries>();
        services.AddScoped<ICoffeeZoneQueries, CoffeeZoneQueries>();

        services.AddScoped<CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate.IReviewReportRepository, ReviewReportRepository>();
        // Repositories
        services.AddScoped<IAppDistributionRepository, AppDistributionRepository>();

        // Query Repository
        services.AddScoped<IQueryCoffeeShopRepository, QueryCoffeeShopRepository>();
        services.AddScoped<ICoffeeShopRepository, CoffeeShopRepository>();
        services.AddScoped<ICoffeeZoneRepository, CoffeeZoneRepository>();
        services.AddScoped<IAdminCoffeeShopQueryRepository, AdminCoffeeShopQueryRepository>();
        services.AddScoped<IQueryCheckInRepository, QueryCheckInRepository>();
        services.AddScoped<IQueryCityRepository, QueryCityRepository>();
        services.AddScoped<IQueryEquipmentRepository, QueryEquipmentRepository>();
        services.AddScoped<IQueryRoasterRepository, QueryRoasterRepository>();
        services.AddScoped<IQueryCoffeeBeanRepository, QueryCoffeeBeanRepository>();
        services.AddScoped<IQueryBrewMethodRepository, QueryBrewMethodRepository>();
        services.AddScoped<ICityRepository, CityRepository>();
        services.AddScoped<IEquipmentRepository, EquipmentRepository>();
        services.AddScoped<IRoasterRepository, RoasterRepository>();
        services.AddScoped<ICoffeeBeanRepository, CoffeeBeanRepository>();
        services.AddScoped<IBrewMethodRepository, BrewMethodRepository>();
        services.AddScoped<IAdminStatsQueryRepository, AdminStatsQueryRepository>();
        services.AddScoped<IPublicStatsQueryRepository, PublicStatsQueryRepository>();
        services.AddScoped<IShopTagRepository, ShopTagRepository>();
        services.AddScoped<IQueryShopTagRepository, QueryShopTagRepository>();
        services.AddScoped<IQueryCoffeeDrinkRepository, QueryCoffeeDrinkRepository>();
        services.AddScoped<ICoffeeDrinkRepository, QueryCoffeeDrinkRepository>();
        services.AddScoped<CoffeePeek.Shared.Kernel.Drinks.IDrinkCatalog, QueryCoffeeDrinkRepository>();
        services.AddScoped<IQueryShopMenuRepository, QueryShopMenuRepository>();
        services.AddScoped<IShopMenuRepository, ShopMenuRepository>();
        services.AddCacheModule();

        return services;
    }
}
