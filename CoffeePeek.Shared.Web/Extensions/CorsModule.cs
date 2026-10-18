using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CoffeePeek.Shared.Web.Extensions;

public static class CorsModule
{
    public static IServiceCollection AddCorsModule(this IServiceCollection services, IHostEnvironment environment)
    {
        var allowedOrigins = Environment.GetEnvironmentVariable("ALLOWED_ORIGINS");
        var origins = string.IsNullOrWhiteSpace(allowedOrigins)
            ? []
            : allowedOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.WithExposedHeaders("ETag", "Date", "Age");
                if (environment.IsDevelopment())
                {
                    policy
                        .SetIsOriginAllowed(origin =>
                        {
                            var host = new Uri(origin).Host;
                            return host == "localhost" ||
                                   host == "127.0.0.1" ||
                                   host.StartsWith("192.168.") ||
                                   host.StartsWith("10.") ||
                                   host.EndsWith(".local");
                        })
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                }
                else
                {
                    if (origins.Length == 0)
                        throw new InvalidOperationException("CORS enabled but ALLOWED_ORIGINS is empty");

                    policy
                        .WithOrigins(origins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                }
            });
        });

        return services;
    }

}
