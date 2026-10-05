using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Extensions;
using CoffeePeek.Shared.Web;
using CoffeePeek.Shared.Web.Extensions;
using CoffeePeek.Shared.Web.Handlers;
using CoffeePeek.Shared.Web.Sentry;

namespace CoffeePeek.ShopsService;

public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BearerSecurityTransformer>();
        });
        
        services.AddControllersModule();
        services.AddOptions<CoffeePeek.Shared.Kernel.Options.CoffeeImportAuthOptions>()
            .BindConfiguration(nameof(CoffeePeek.Shared.Kernel.Options.CoffeeImportAuthOptions))
            .Validate(o => o.Token.Length == 0 || o.Token.Length >= 32).ValidateOnStart();
        services.AddResponseCaching();
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
        builder.UseCoffeePeekSentry();

        return builder;
    }
}

