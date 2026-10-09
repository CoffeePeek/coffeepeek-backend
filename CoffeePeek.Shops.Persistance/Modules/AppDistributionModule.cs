using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;
using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeePeek.Shops.Persistance.Modules;

/// <summary>Registers the AppDistribution persistence boundary within the Shops process.</summary>
public static class AppDistributionModule
{
    public static IServiceCollection AddAppDistributionModule(this IServiceCollection services)
    {
        services.AddScoped<IAppDistributionRepository, AppDistributionRepository>();
        return services;
    }
}
