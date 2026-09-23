using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sentry.Serilog;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace CoffeePeek.Shared.Web.Logging;

public static class SerilogExtensions
{
    private const string DefaultTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}";

    public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder)
    {
        var isDev = builder.Environment.IsDevelopment();
        var theme = isDev ? AnsiConsoleTheme.Code : ConsoleTheme.None;

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .Enrich.With<SensitiveDataRedactingEnricher>()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Yarp.ReverseProxy.Health", LogEventLevel.Error)
            .WriteTo.Console(outputTemplate: DefaultTemplate, theme: theme)
            .WriteTo.Sentry(SentrySerilogSink)
            .CreateLogger();

        builder.Host.UseSerilog();

        return builder;
    }

    public static HostApplicationBuilder AddSerilogLogging(this HostApplicationBuilder builder)
    {
        var isDev = builder.Environment.IsDevelopment();
        var theme = isDev ? AnsiConsoleTheme.Code : ConsoleTheme.None;

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .Enrich.With<SensitiveDataRedactingEnricher>()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            // Active health check failures are expected and self-healing on every backend
            // restart — logging them at Warning turned every deploy into a Sentry issue.
            .MinimumLevel.Override("Yarp.ReverseProxy.Health", LogEventLevel.Error)
            .WriteTo.Console(outputTemplate: DefaultTemplate, theme: theme)
            .WriteTo.Sentry(SentrySerilogSink)
            .CreateLogger();

        builder.Services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSerilog();
        });

        return builder;
    }

    /// <summary>
    /// Forwards Serilog events into Sentry Logs. SDK is initialized by UseCoffeePeekSentry.
    /// </summary>
    private static void SentrySerilogSink(SentrySerilogOptions options)
    {
        options.InitializeSdk = false;
        options.EnableLogs = true;
        options.MinimumBreadcrumbLevel = LogEventLevel.Debug;
        options.MinimumEventLevel = LogEventLevel.Warning;
    }
}
