using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Data;
using CoffeePeek.MediaService.Handlers;
using CoffeePeek.MediaService.Repositories;
using CoffeePeek.MediaService.Services;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Auth.Extensions;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Extentions;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shared.Persistence;
using CoffeePeek.Shared.Persistence.Data;
using CoffeePeek.Shared.Persistence.Extensions;
using CoffeePeek.Shared.Web;
using CoffeePeek.Shared.Web.Handlers;
using CoffeePeek.Shared.Web.Sentry;
using System.Text.Json.Serialization;
using CoffePeek.ServiceDefaults;

namespace CoffeePeek.MediaService;

public static class InfrastructureExtensions
{
    public static WebApplicationBuilder AddApplication(this WebApplicationBuilder builder)
    {
        // OpenTelemetry owns HTTP tracing; its filters/suppression protect signed storage/renderer URLs.
        builder.WebHost.UseCoffeePeekSentry(options => options.DisableSentryHttpMessageHandler = true);
        builder.ConfigureOpenTelemetry();
        var services = builder.Services;

        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
        services.AddProblemDetails();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<CheckInStorageState>();
        services.AddSingleton<CheckInStorageMonitor>();
        services.AddHostedService(sp => sp.GetRequiredService<CheckInStorageMonitor>());
        services.AddHealthChecks().AddCheck<CheckInStorageMonitor>("checkin-private-storage");
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddHeaderUserContext(builder.Configuration);

        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BearerSecurityTransformer>();
        });

        services.AddAuthorizationBuilder()
            .AddPolicy(RoleConsts.Admin, policy => policy.RequireRole(RoleConsts.Admin))
            .AddPolicy(RoleConsts.User, policy => policy.RequireRole(RoleConsts.User));

        if (builder.Environment.IsDevelopment())
        {
            builder.AddNpgsqlDbContext<MediaDbContext>(
                connectionName: AppResources.MediaDb,
                configureDbContextOptions: opt => opt.AddInterceptors(new AuditInterceptor()),
                configureSettings: settings => { settings.DisableRetry = true; }
            );
        }
        else
        {
            var connectionString = services.AddValidateOptions<PostgresCpOptions>().ConnectionString;

            services.AddDatabase<MediaDbContext>(
                connectionString,
                opt => opt.AddInterceptors(new AuditInterceptor())
            );
        }

        services.AddScoped<IUnitOfWork, UnitOfWork<MediaDbContext>>();
        services.AddScoped<IPhotoRepository, PhotoRepository>();
        services.AddScoped<ClaimCheckInPhotosHandler>();
        services.AddScoped<ClaimShopPhotosHandler>();

        services.AddOptions<MediaPublicUrlOptions>()
            .BindConfiguration(nameof(MediaPublicUrlOptions));

        services.AddValidateOptions<MinIOOptions>();

        services.AddScoped<IStorageService, MinIOStorageService>();
        services.AddOptions<CheckInPhotoReadOptions>().BindConfiguration(nameof(CheckInPhotoReadOptions))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddScoped<ICheckInPhotoReader, CheckInPhotoReader>();
        services.AddOptions<CoffeeImportAuthOptions>().BindConfiguration(nameof(CoffeeImportAuthOptions))
            .Validate(o => o.Token.Length == 0 || o.Token.Length >= 32).ValidateOnStart();

        var handlersAssembly = typeof(ConfirmPhotoHandler).Assembly;
        builder.AddWolverine(
            typeof(InfrastructureExtensions).Assembly,
            [handlersAssembly],
            [typeof(MediaDbContext)]);

        return builder;
    }

    public static void UseApplication(this WebApplication app)
    {
        app.MapOpenApi();

        app.UseExceptionHandler();
        app.MapHealthChecks("/health");
        app.MapControllers();
    }
}
