using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.Admin.Stats;
using CoffeePeek.Account.Infrastructure.Admin;
using CoffeePeek.Account.Infrastructure.Media;
using CoffeePeek.Account.Infrastructure.Options;
using CoffeePeek.Account.Domain.Services;
using CoffeePeek.Account.Infrastructure.Consumers;
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
        services.AddValidateOptions<MinIOOptions>();

        services.AddScoped<IMediaUrlProvider, MediaUrlProvider>();

        // 1. Domain Services (реализации интерфейсов из Domain)
        services.AddScoped<IPasswordHasherService, PasswordHasherService>();
        services.AddScoped<IJWTTokenService, JWTTokenService>();
        services.AddSingleton<IAccountDeletionTokenService, AccountDeletionTokenService>();

        // 2. Event Handlers
        services.AddScoped<CheckinCreatedHandler>();
        services.AddScoped<ModerationShopApprovedAccountHandler>();
        services.AddScoped<ModerationRoasterApprovedAccountHandler>();
        services.AddScoped<UserPhotoUploadedHandler>();
        services.AddScoped<UserRegisteredEventHandler>();
        services.AddScoped<PasswordResetRequestedEventHandler>();
        services.AddScoped<AccountDeletionRequestedEventHandler>();

        // 3 Email Service
        services.AddHttpClient<ResendClient>();
        var resendOptions = services.AddValidateOptions<ResendClientOptions>();
        services.Configure<ResendClientOptions>(o =>
        {
            o.ApiToken = resendOptions.ApiToken;
        });
        services.AddTransient<IResend, ResendClient>();

        // 4. OAuth
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

        services.AddOptions<CoffeePeek.Account.Infrastructure.Notifications.PushNotificationOptions>()
            .BindConfiguration("PushNotifications")
            .ValidateDataAnnotations()
            .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.ProjectId), "PushNotifications:ProjectId is required when enabled.")
            .ValidateOnStart();
        services.AddSingleton(_ => Google.Apis.Auth.OAuth2.GoogleCredential.GetApplicationDefault()
            .CreateScoped("https://www.googleapis.com/auth/firebase.messaging"));

        return services;
    }
}
