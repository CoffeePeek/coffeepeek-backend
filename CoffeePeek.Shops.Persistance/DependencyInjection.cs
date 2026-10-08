using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Extentions;
using CoffeePeek.Shared.Persistence;
using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Shared.Persistence.Extensions;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Application.Features.CoffeeZones;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Modules;
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
        services.Configure<PublicAddressOptions>(builder.Configuration.GetSection("PublicAddresses"));
        services.Configure<CoffeePeek.Shops.Application.Features.CoffeeCatalog.CoffeeImportOptions>(builder.Configuration.GetSection("CoffeeImportOptions"));
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


        services.AddShopsModule();
        services.AddMenuModule();
        services.AddCommunityModule();
        services.AddCoffeeCatalogModule();
        services.AddDiscoveryModule();
        services.AddAppDistributionModule();
        services.AddCacheModule();

        return services;
    }
}
