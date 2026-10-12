using CoffeePeek.AccountService.Extensions;
using CoffeePeek.AccountService.Realtime;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Extensions;
using CoffeePeek.Shared.Web;
using CoffeePeek.Shared.Web.Extensions;
using CoffeePeek.Shared.Web.Handlers;
using CoffeePeek.Shared.Web.Sentry;

namespace CoffeePeek.AccountService;

public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTrustedProxyForwarding(configuration);
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BearerSecurityTransformer>();
        });

        services.AddControllersModule();
        services.AddSignalR();
        services.AddScoped<ISessionTerminationNotifier, SignalRSessionTerminationNotifier>();

        services.AddHeaderUserContext(configuration);

        services.AddAuthorizationBuilder()
            .AddPolicy(RoleConsts.Admin, policy => policy.RequireRole(RoleConsts.Admin))
            .AddPolicy(RoleConsts.Moderator, policy => policy.RequireRole(RoleConsts.Moderator, RoleConsts.Admin))
            .AddPolicy(RoleConsts.User, policy => policy.RequireRole(RoleConsts.User));

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        return services;
    }

    public static IWebHostBuilder ConfigureWebhost(this IWebHostBuilder builder)
    {
        builder.ConfigureEnvironment();
        builder.UseCoffeePeekSentry(options =>
            options.SetBeforeSend(SentryExtensions.FilterExpectedClientErrors));
        return builder;
    }
}
