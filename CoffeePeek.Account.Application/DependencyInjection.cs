using CoffeePeek.Account.Application.Common;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Common.Options;
using CoffeePeek.Account.Application.Features.Auth.Login;
using CoffeePeek.Account.Application.Features.Auth.OAuthLogin;
using CoffeePeek.Account.Application.Mapper;
using CoffeePeek.Account.Domain.Services;
using CoffeePeek.Shared.Kernel.Options;
using MapsterMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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

        services.AddSingleton<IMapper>(sp =>
            MapsterConfiguration.CreateMapper(sp.GetRequiredService<IOptions<MediaPublicUrlOptions>>().Value));
        
        services.AddScoped<IAuthService, AuthService>();

        services.AddSingleton<EmailExistenceFilter>(_ =>
        {
            const int expectedCount = 1000000;
            const double errorRate = 0.01;

            return new EmailExistenceFilter(expectedCount, errorRate);
        });

        services.AddSingleton<TimeProvider>(TimeProvider.System);
        
        services.AddScoped<IExternalAuthService, ExternalAuthService>();
        
        return services;
    }
}