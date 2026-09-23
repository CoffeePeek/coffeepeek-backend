using CoffeePeek.Account.Application;
using CoffeePeek.Account.Application.Features.Auth.RegisterUser;
using CoffeePeek.AccountService.Realtime;
using CoffeePeek.Account.Infrastructure;
using CoffeePeek.Account.Infrastructure.Consumers;
using CoffeePeek.Account.Persistence;
using CoffeePeek.Account.Persistence.Configuration;
using CoffeePeek.Shared.Persistence.Extensions;
using CoffeePeek.Shared.Web.Logging;
using CoffePeek.ServiceDefaults;
using Serilog;
using Resend;

namespace CoffeePeek.AccountService;

public static class InfrastructureExtensions
{
    public static WebApplicationBuilder AddApplication(this WebApplicationBuilder builder)
    {
        builder.AddSerilogLogging();
        builder.AddServiceDefaults();
        builder.WebHost
            .ConfigureWebhost();
        
        var applicationAssembly = typeof(RegisterUserHandler).Assembly;
        var infrastructureAssembly = typeof(CheckinCreatedHandler).Assembly;
        builder.AddWolverine(
            typeof(InfrastructureExtensions).Assembly,
            [applicationAssembly, infrastructureAssembly],
            [typeof(AccountDbContext), typeof(IResend)]);
        
        builder.Services
            .AddApplication()
            .AddInfrastructure()
            .AddPersistence(builder)
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

        app.MapHub<SessionHub>("/realtime/session");
        app.MapControllers();
    }
}
