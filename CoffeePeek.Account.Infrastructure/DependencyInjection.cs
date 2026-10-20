using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.Admin.Stats;
using CoffeePeek.Account.Infrastructure.Admin;
using CoffeePeek.Account.Infrastructure.Options;
using CoffeePeek.Account.Domain.Services;
using CoffeePeek.Account.Infrastructure.Identity;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Extentions;
using Microsoft.Extensions.DependencyInjection;
using Resend;

namespace CoffeePeek.Account.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddValidateOptions<JWTOptions>();

        services.AddScoped<IPasswordHasherService, PasswordHasherService>();
        services.AddScoped<IJWTTokenService, JWTTokenService>();
        services.AddSingleton<IAccountDeletionTokenService, AccountDeletionTokenService>();

        // Wolverine constructs message handlers itself; they need no registration.
        services.AddHttpClient<ResendClient>();
        services.AddValidateOptions<ResendClientOptions>();
        services.AddTransient<IResend, ResendClient>();

        services.AddOptions<OAuthGoogleOptions>()
            .BindConfiguration(nameof(OAuthGoogleOptions));
        services.AddScoped<IGoogleAuthService, GoogleAuthService>();
        services.AddOptions<OAuthAppleOptions>().BindConfiguration(nameof(OAuthAppleOptions))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId), "Apple ClientId is required").ValidateOnStart();
        services.AddSingleton<Microsoft.IdentityModel.Protocols.IConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>>(
            _ => new Microsoft.IdentityModel.Protocols.ConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(
                "https://appleid.apple.com/.well-known/openid-configuration",
                new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfigurationRetriever()));
        services.AddScoped<IAppleAuthService, AppleAuthService>();
        services.AddSingleton<IAppleLoginChallengeStore, AppleLoginChallengeStore>();
        services.AddHttpClient<IAppleWebAuthService, AppleWebAuthService>(client => client.Timeout = TimeSpan.FromSeconds(15));

        services.AddOptions<AdminStatsOptions>()
            .BindConfiguration(nameof(AdminStatsOptions));

        services.AddHttpClient("admin-stats");
        services.AddScoped<IAdminStatsClient, AdminStatsClient>();

        return services;
    }
}
