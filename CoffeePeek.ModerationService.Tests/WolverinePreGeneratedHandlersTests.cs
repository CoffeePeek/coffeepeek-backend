using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Wolverine.Runtime.Handlers;

namespace CoffeePeek.ModerationService.Tests;

// Regression test for a production crash-loop: TypeLoadMode.Static (non-Development) requires
// pre-generated Wolverine handler code compiled into the assembly (Internal/Generated/WolverineHandlers/),
// and that directory silently fell out of sync whenever a new handler was added without re-running
// `dotnet run -- codegen write`. WolverineRuntime.StartAsync asserts pre-built types exist before
// touching Postgres/RabbitMQ, so pointing at unreachable infra still exercises the real check without
// needing live services — any exception other than MissingPreBuiltTypesException means we got past it.
public class WolverinePreGeneratedHandlersTests
{
    [Fact]
    public async Task ProductionModerationHost_StartsPastWolverineHandlerCheck()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ApplicationName = typeof(InfrastructureExtensions).Assembly.FullName
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sentry:Dsn"] = "",
            ["PostgresCpOptions:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=codegen_test;Username=test;Password=test;Timeout=1",
            ["RabbitMqOptions:HostName"] = "127.0.0.1",
            ["RabbitMqOptions:Port"] = "1",
            ["RabbitMqOptions:Username"] = "guest",
            ["RabbitMqOptions:Password"] = "guest",
            ["YandexApiOptions:ApiKey"] = "codegen-test",
            ["YandexApiOptions:BaseUrl"] = "https://geocode-maps.yandex.ru/v1/",
            ["YandexApiOptions:TimeoutSeconds"] = "30",
            ["GooglePlaces:ApiKey"] = "codegen-test",
            ["GooglePlaces:BaseUrl"] = "https://places.googleapis.com/v1/",
            ["GooglePlaces:TimeoutSeconds"] = "15",
            ["GooglePlaces:CacheDays"] = "30",
            ["GooglePlaces:MaxDistanceMeters"] = "250",
            ["MinIOOptions:Endpoint"] = "http://localhost:9000",
            ["MinIOOptions:AccessKey"] = "test",
            ["MinIOOptions:SecretKey"] = "test",
            ["MinIOOptions:BucketName"] = "coffee.shops",
            ["GatewayAuth:SecretKey"] = "codegen-test-gateway-secret-at-least-32-characters",
            ["MediaPublicUrlOptions:PublicEndpoint"] = "http://localhost:9000",
            ["MediaPublicUrlOptions:ShopBucketName"] = "coffeepeek.shops",
            ["MediaPublicUrlOptions:AvatarBucketName"] = "coffeepeek.avatars"
        });
        builder.AddApplication();

        await using var app = builder.Build();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            await app.StartAsync(cts.Token);
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
        catch (MissingPreBuiltTypesException ex)
        {
            Assert.Fail(
                "Internal/Generated/WolverineHandlers/ is out of sync with the current handlers. " +
                $"Run 'dotnet run -- codegen write' in CoffeePeek.ModerationService and commit the output. {ex.Message}");
        }
        catch
        {
            // Any other failure (e.g. unreachable Postgres/RabbitMQ) means Wolverine's pre-built
            // type check already passed — that's all this test verifies.
        }
    }
}
