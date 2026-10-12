using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Extensions;
using CoffeePeek.Shared.Web;
using CoffeePeek.Shared.Web.Extensions;
using CoffeePeek.Shared.Web.Handlers;
using CoffeePeek.Shared.Web.Sentry;
using CoffeePeek.ModerationService.Telegram;

namespace CoffeePeek.ModerationService;

public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BearerSecurityTransformer>();
        });
        
        services.AddControllersModule();
        services.AddHostedService<TelegramModerationPoller>();

        services.AddHeaderUserContext(configuration);

        // Authorization policies (JWT validation happens in Gateway)
        services.AddAuthorizationBuilder()
            .AddPolicy(RoleConsts.Admin, policy => policy.RequireRole(RoleConsts.Admin))
            .AddPolicy(RoleConsts.Owner, policy => policy.RequireRole(RoleConsts.Owner))
            .AddPolicy(RoleConsts.User, policy => policy.RequireRole(RoleConsts.User))
            .AddPolicy(RoleConsts.Moderator, policy => policy.RequireRole(RoleConsts.Moderator, RoleConsts.Admin))
            .AddPolicy(RoleConsts.Employee, policy => policy.RequireRole(RoleConsts.Employee))
            .AddPolicy(RoleConsts.Roaster, policy => policy.RequireRole(RoleConsts.Roaster));

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        return services;
    }

    public static IWebHostBuilder ConfigureWebhost(this IWebHostBuilder builder)
    {
        builder.ConfigureEnvironment();
        // OpenTelemetry filters Telegram URLs; Sentry's separate HTTP handler would expose the bot token.
        builder.UseCoffeePeekSentry(options => options.DisableSentryHttpMessageHandler = true);

        return builder;
    }
}
