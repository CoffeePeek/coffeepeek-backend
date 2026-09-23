using System.Reflection;
using CoffeePeek.ShopsService;
using CoffeePeek.Shops.Application.Features.CoffeeShop.GetCoffeeShop;
using CoffeePeek.Shops.Infrastructure.Consumers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Wolverine.Runtime.Handlers;

namespace CoffeePeek.ShopsService.Tests;

// Regression test for a production crash-loop: TypeLoadMode.Static (non-Development) requires
// pre-generated Wolverine handler code compiled into the assembly (Internal/Generated/WolverineHandlers/),
// and that directory silently falls out of sync whenever a new handler is added without re-running
// `dotnet run -- codegen write`. WolverineRuntime.StartAsync asserts pre-built types exist before
// touching Postgres/RabbitMQ, so pointing at unreachable infra still exercises the real check without
// needing live services — any exception other than MissingPreBuiltTypesException means we got past it.
public class WolverinePreGeneratedHandlersTests
{
    [Fact]
    public async Task ProductionShopsHost_StartsPastWolverineHandlerCheck()
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
            ["RedisOptions:Host"] = "127.0.0.1",
            ["RedisOptions:Port"] = "1",
            ["RedisOptions:Password"] = "test",
            ["GatewayAuth:SecretKey"] = "codegen-test-gateway-secret-at-least-32-characters",
            ["MediaPublicUrlOptions:PublicEndpoint"] = "http://localhost:9000",
            ["MediaPublicUrlOptions:ShopBucketName"] = "coffeepeek.shops",
            ["MediaPublicUrlOptions:AvatarBucketName"] = "coffeepeek.avatars",
            ["OpenAiOptions:TimeoutSeconds"] = "90"
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
                $"Run 'dotnet run -- codegen write' in CoffeePeek.ShopsService and commit the output. {ex.Message}");
        }
        catch
        {
            // Any other failure (e.g. unreachable Postgres/RabbitMQ) means Wolverine's pre-built
            // type check already passed — that's all this test verifies.
        }
    }

    // Complements the start-up test above, which only catches DANGLING pre-generated references
    // (MissingPreBuiltTypesException). It does NOT catch a NEWLY-ADDED handler that was never
    // codegen'd — that handler is simply absent from the manifest, so in TypeLoadMode.Static the
    // host has no local executor for its message and Wolverine falls back to remote request/reply,
    // which times out (5s) at runtime instead of failing at startup. (Regression: GetRoasterByIdQuery
    // shipped without pre-generated code and 500'd in production.) This test closes that gap: every
    // handler Wolverine discovers must have a matching pre-generated class compiled into the assembly.
    [Fact]
    public void EveryDiscoveredHandler_HasPreGeneratedCode()
    {
        // Same assemblies the host scans for handlers (see InfrastructureExtensions.AddApplication).
        Assembly[] handlerAssemblies =
        [
            typeof(GetCoffeeShopHandler).Assembly,          // CoffeePeek.Shops.Application
            typeof(ModerationShopApproveHandler).Assembly,  // CoffeePeek.Shops.Infrastructure
        ];

        var handlerMethodNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Handle", "HandleAsync", "Handles", "Consume", "Consumes", "ConsumeAsync"
        };

        // Message type per discovered handler method (Wolverine convention: first parameter is the
        // message). Restricted to CoffeePeek message types to skip helper methods on *Handler classes.
        var discoveredMessageNames = handlerAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && (t.Name.EndsWith("Handler", StringComparison.Ordinal)
                            || t.Name.EndsWith("Consumer", StringComparison.Ordinal)))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => handlerMethodNames.Contains(m.Name))
            .Select(m => m.GetParameters().FirstOrDefault()?.ParameterType)
            .Where(pt => pt?.Namespace?.StartsWith("CoffeePeek", StringComparison.Ordinal) == true)
            .Select(pt => pt!.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Wolverine names each pre-generated class "{MessageTypeName}Handler{hash}" in this namespace.
        var generatedNames = typeof(InfrastructureExtensions).Assembly
            .GetTypes()
            .Where(t => t.Namespace == "Internal.Generated.WolverineHandlers")
            .Select(t => t.Name)
            .ToArray();

        var missing = discoveredMessageNames
            .Where(msg => !generatedNames.Any(g => g.StartsWith(msg + "Handler", StringComparison.Ordinal)))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.True(missing.Length == 0,
            "Internal/Generated/WolverineHandlers/ is out of sync — missing pre-generated code for: "
            + string.Join(", ", missing)
            + ". Run 'dotnet run -- codegen write' in CoffeePeek.ShopsService and commit the output.");
    }
}
