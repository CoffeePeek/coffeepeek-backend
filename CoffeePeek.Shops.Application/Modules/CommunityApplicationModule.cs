using CoffeePeek.Shops.Application.PublicAddresses;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeePeek.Shops.Application.Modules;

public static class CommunityApplicationModule
{
    public static IServiceCollection AddCommunityApplicationModule(this IServiceCollection services)
    {
        services.AddScoped<IPublicCheckInProjector, PublicCheckInProjector>();
        return services;
    }
}
