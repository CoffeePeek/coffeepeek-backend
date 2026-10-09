using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;
using CoffeePeek.Shops.Persistance.CoffeeCatalog;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CoffeePeek.ShopsService.Tests.Architecture;

public sealed class CoffeeCatalogBoundaryTests
{
    [Fact]
    public void RegisteredWritersAreApplicationScenarios_AndReadersShareOneScopedProjection()
    {
        var services = new ServiceCollection();
        services.AddLogging(); services.AddOptions<CoffeeImportOptions>();
        services.AddOptions<MediaPublicUrlOptions>().Configure(o => o.PublicEndpoint = "https://media.example.test");
        services.AddScoped(_ => new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=test;Username=test;Password=test").Options));
        services.AddSingleton(Mock.Of<IPublicAddressService>());
        services.AddCoffeeCatalogModule();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        Assert.IsType<CoffeeProductWriter>(scope.ServiceProvider.GetRequiredService<ICoffeeProductWriter>());
        Assert.IsType<CoffeeCatalogTagWriter>(scope.ServiceProvider.GetRequiredService<ICoffeeCatalogTagWriter>());
        Assert.IsType<CoffeeImportService>(scope.ServiceProvider.GetRequiredService<ICoffeeImportWriter>());
        Assert.Same(scope.ServiceProvider.GetRequiredService<IPublicCoffeeCatalogReader>(),
            scope.ServiceProvider.GetRequiredService<IAdminCoffeeCatalogReader>());
    }

    [Fact]
    public void PersistenceDoesNotChooseManualPriorityOrImportLifecycle()
    {
        string[] decisions = [nameof(CoffeeProduct.IsProtected), nameof(CoffeeProduct.ApplyImportedCountries),
            nameof(CoffeeImportRun.MarkApplied), nameof(CoffeeImportRun.MarkFailed), nameof(CoffeeImportRun.CanApplyPayload)];
        var calls = IlDependencyScanner.GetCalls(typeof(CoffeeCatalogStore).Assembly,
            type => type.Namespace?.StartsWith("CoffeePeek.Shops.Persistance.CoffeeCatalog", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(calls, call => decisions.Contains(call.Callee.Name) &&
            call.Callee.DeclaringType?.Namespace?.Contains("CoffeeProductAggregate", StringComparison.Ordinal) == true);
    }
}
