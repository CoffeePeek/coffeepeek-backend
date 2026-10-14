using System.Reflection;
using CoffeePeek.Account.Application.Features.Auth.AppleLogin;
using CoffeePeek.Account.Application.Features.Auth.RegisterUser;
using CoffeePeek.Account.Infrastructure.Consumers;
using CoffeePeek.AccountService;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Wolverine.Runtime.Handlers;

namespace CoffeePeek.Account.Infrastructure.Tests;

// Regression test for a production crash-loop: TypeLoadMode.Static (non-Development) requires
// pre-generated Wolverine handler code compiled into the assembly (Internal/Generated/WolverineHandlers/),
// and that directory silently falls out of sync whenever a new handler is added without re-running
// `dotnet run -- codegen write`. WolverineRuntime.StartAsync asserts pre-built types exist before
// touching Postgres/RabbitMQ, so pointing at unreachable infra still exercises the real check without
// needing live services — any exception other than MissingPreBuiltTypesException means we got past it.
public class WolverinePreGeneratedHandlersTests
{
    [Fact]
    public void AppleLoginHandler_HasPreGeneratedCodeAndRegistryEntry()
    {
        var accountAssembly = typeof(InfrastructureExtensions).Assembly;
        var registryType = accountAssembly.GetType("Internal.Generated.WolverineHandlers.GeneratedHandlerRegistry");
        Assert.NotNull(registryType);
        var registry = (HandlerRegistry)Activator.CreateInstance(registryType)!;
        var handlerTypes = registry.HandlerTypes();
        Assert.Contains(typeof(AppleLoginHandler), handlerTypes);

        var generatedAdapterNames = accountAssembly.GetTypes()
            .Where(t => t.Namespace == "Internal.Generated.WolverineHandlers")
            .Select(t => t.Name);
        Assert.Contains(generatedAdapterNames,
            name => name.StartsWith(nameof(AppleLoginCommand) + "Handler", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryDiscoveredHandler_HasPreGeneratedCode()
    {
        // Match the handler assemblies registered by AccountService.InfrastructureExtensions.
        Assembly[] handlerAssemblies =
        [
            typeof(RegisterUserHandler).Assembly,
            typeof(CheckinCreatedHandler).Assembly
        ];

        var handlerMethodNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Handle", "HandleAsync", "Handles", "Consume", "Consumes", "ConsumeAsync"
        };

        var discoveredMessageNames = handlerAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && (!t.IsAbstract || t.IsSealed)
                        && (t.Name.EndsWith("Handler", StringComparison.Ordinal)
                            || t.Name.EndsWith("Consumer", StringComparison.Ordinal)))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => handlerMethodNames.Contains(m.Name))
            .Select(m => m.GetParameters().FirstOrDefault()?.ParameterType)
            .Where(pt => pt?.Namespace?.StartsWith("CoffeePeek", StringComparison.Ordinal) == true)
            .Select(pt => pt!.Name)
            .ToHashSet(StringComparer.Ordinal);

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
            + ". Run 'dotnet run -- codegen write' in CoffeePeek.AccountService and commit the output.");
    }

    [Fact]
    public async Task ProductionAccountHost_StartsPastWolverineHandlerCheck()
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
            ["JWTOptions:SecretKey"] = "codegen-test-secret-at-least-32-characters",
            ["JWTOptions:Issuer"] = "CoffeePeek.WEB",
            ["JWTOptions:Audience"] = "CoffeePeek.API",
            ["JWTOptions:AccessTokenLifetimeMinutes"] = "60",
            ["JWTOptions:RefreshTokenLifetimeDays"] = "7",
            ["GatewayAuth:SecretKey"] = "codegen-test-gateway-secret-at-least-32-characters",
            ["MinIOOptions:Endpoint"] = "http://localhost:9000",
            ["MinIOOptions:AccessKey"] = "test",
            ["MinIOOptions:SecretKey"] = "test",
            ["MediaPublicUrlOptions:PublicEndpoint"] = "http://localhost:9000",
            ["MediaPublicUrlOptions:ShopBucketName"] = "coffeepeek.shops",
            ["MediaPublicUrlOptions:AvatarBucketName"] = "coffeepeek.avatars",
            ["ResendClientOptions:ApiToken"] = "re_test_placeholder",
            ["AccountDeletionOptions:TokenPepper"] = "test-account-deletion-pepper-at-least-32-characters"
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
                $"Run 'dotnet run -- codegen write' in CoffeePeek.AccountService and commit the output. {ex.Message}");
        }
        catch
        {
            // Any other failure (e.g. unreachable Postgres/RabbitMQ) means Wolverine's pre-built
            // type check already passed — that's all this test verifies.
        }
    }
}
