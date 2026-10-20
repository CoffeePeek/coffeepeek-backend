using CoffeePeek.Account.Application.Common.Options;
using CoffeePeek.Account.Application.Features.Auth.OAuthLogin;
using CoffeePeek.Account.Domain.Services;
using CoffeePeek.Shared.Kernel.Options;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeePeek.Account.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<AccountDeletionOptions>()
            .BindConfiguration(nameof(AccountDeletionOptions))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<MediaPublicUrlOptions>()
            .BindConfiguration(nameof(MediaPublicUrlOptions))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<TimeProvider>(TimeProvider.System);

        services.AddScoped<IExternalAuthService, ExternalAuthService>();

        return services;
    }
}
