using CoffeePeek.Moderation.Application;
using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Moderation.Application.Features.Shop.CreateShop;
using CoffeePeek.Moderation.Infrastructure;
using CoffeePeek.Moderation.Infrastructure.Consumers;
using CoffeePeek.Moderation.Infrastructure.Notifications;
using CoffeePeek.Shared.Persistence.Extensions;
using CoffeePeek.Shared.Kernel.Geocoding;
using CoffeePeek.Shared.Web.Logging;
using CoffeeShop.Moderation.Persistence;
using CoffeeShop.Moderation.Persistence.Configuration;
using CoffePeek.ServiceDefaults;
using Serilog;

namespace CoffeePeek.ModerationService;

public static class InfrastructureExtensions
{
    public static WebApplicationBuilder AddApplication(this WebApplicationBuilder builder)
    {
        builder.AddSerilogLogging();
        builder.AddServiceDefaults();
        builder.WebHost
            .ConfigureWebhost();
        var applicationAssembly = typeof(SendCoffeeShopToModerationHandler).Assembly;
        var infrastructureAssembly = typeof(CheckInModerationRequestedHandler).Assembly;
        builder.AddWolverine(
            typeof(InfrastructureExtensions).Assembly,
            [applicationAssembly, infrastructureAssembly],
            [typeof(ModerationDbContext), typeof(IAddressGeocoder), typeof(IGooglePlacesLookup), typeof(IOverpassClient), typeof(TelegramModerationClient)]);

        builder.Services
            .AddApplication()
            .AddPersistence(builder)
            .AddInfrastructure()
            .AddPresentation(builder.Configuration);

        return builder;
    }

    public static void UseApplication(this WebApplication app)
    {
        app.UseCoffeePeekRequestLogging();

        app.UseExceptionHandler();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapOpenApi();

        app.MapDefaultEndpoints();


        app.MapControllers();
    }
}
