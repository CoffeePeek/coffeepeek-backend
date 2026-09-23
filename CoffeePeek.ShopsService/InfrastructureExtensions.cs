using CoffeePeek.Shops.Application;
using CoffeePeek.Shops.Infrastructure;
using CoffeePeek.Shops.Persistance;
using CoffeePeek.Shared.Persistence.Extensions;
using CoffeePeek.Shared.Web.Logging;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Infrastructure.Consumers;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffePeek.ServiceDefaults;
using Serilog;

namespace CoffeePeek.ShopsService;

public static class InfrastructureExtensions
{
    public static WebApplicationBuilder AddApplication(this WebApplicationBuilder builder)
    {
        builder.AddSerilogLogging();
        builder.AddServiceDefaults();
        builder.WebHost
            .ConfigureWebhost();

        var applicationAssembly = typeof(GetCoffeeShopHandler).Assembly;
        var infrastructureAssembly = typeof(ModerationShopApproveHandler).Assembly;
        builder.AddWolverine(
            typeof(InfrastructureExtensions).Assembly,
            [applicationAssembly, infrastructureAssembly],
            [typeof(ShopsDbContext)]);
        
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
