using System.Text;
using CoffeePeek.Gateway.Authentication;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel.Extentions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace CoffeePeek.Gateway.Extensions;

/// <summary>
/// Extension methods for configuring JWT authentication and role-based authorization policies
/// at the Gateway level.
/// </summary>
public static class AuthExtensions
{
    public const string AppReleaseAutomationScheme = nameof(AppReleaseAutomationScheme);
    public const string AppReleaseAutomationPolicy = nameof(AppReleaseAutomationPolicy);

    /// <summary>
    /// Registers JWT Bearer authentication and all role-based authorization policies.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="environment">
    ///     The host environment — used to disable HTTPS metadata requirement in Development.
    ///     Uses <see cref="IWebHostEnvironment"/> (not <c>Environment.GetEnvironmentVariable</c>)
    ///     so the value is consistent with the ASP.NET Core hosting model.
    /// </param>
    public static IServiceCollection AddGatewayAuth(
        this IServiceCollection services,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        services.AddOptions<GatewayAuthOptions>()
            .Bind(configuration.GetSection(GatewayAuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var authOptions = services.AddValidateOptions<JWTOptions>();
        services.AddScoped<UserRevocationTokenValidator>();

        services.AddOptions<AppReleaseAutomationOptions>()
            .Bind(configuration.GetSection(AppReleaseAutomationOptions.SectionName));

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(x =>
        {
            x.RequireHttpsMetadata = !environment.IsDevelopment();
            x.SaveToken = true;
            x.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    if (!string.IsNullOrWhiteSpace(accessToken)
                        && context.HttpContext.Request.Path.StartsWithSegments("/realtime/session"))
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                },
                OnTokenValidated = async context =>
                {
                    try
                    {
                        var validator = context.HttpContext.RequestServices
                            .GetRequiredService<UserRevocationTokenValidator>();
                        if (!await validator.IsAcceptedAsync(
                                context.Principal!,
                                context.HttpContext.RequestAborted))
                            context.Fail("The access token is no longer valid.");
                    }
                    catch
                    {
                        context.Fail("Access-token revocation could not be verified.");
                    }
                }
            };
            x.TokenValidationParameters = new TokenValidationParameters
            {
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.SecretKey)),
                ValidIssuer = authOptions.Issuer,
                ValidAudience = authOptions.Audience,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.Zero,
                RoleClaimType = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role",
            };
        })
        .AddScheme<AuthenticationSchemeOptions, AppReleaseAutomationAuthenticationHandler>(
            AppReleaseAutomationScheme,
            null);

        services.AddAuthorizationBuilder()
            .AddPolicy("Authenticated",       policy => policy.RequireAuthenticatedUser())
            .AddPolicy(RoleConsts.Admin,      policy => policy.RequireRole(RoleConsts.Admin))
            .AddPolicy(RoleConsts.Owner,      policy => policy.RequireRole(RoleConsts.Owner))
            .AddPolicy(RoleConsts.User,       policy => policy.RequireRole(RoleConsts.User))
            .AddPolicy(RoleConsts.Moderator,  policy => policy.RequireRole(RoleConsts.Moderator, RoleConsts.Admin))
            .AddPolicy(RoleConsts.Employee,   policy => policy.RequireRole(RoleConsts.Employee))
            .AddPolicy(RoleConsts.Roaster,    policy => policy.RequireRole(RoleConsts.Roaster))
            .AddPolicy(AppReleaseAutomationPolicy, policy =>
            {
                policy.AddAuthenticationSchemes(
                    JwtBearerDefaults.AuthenticationScheme,
                    AppReleaseAutomationScheme);
                policy.RequireRole(RoleConsts.Admin);
            });

        return services;
    }
}
