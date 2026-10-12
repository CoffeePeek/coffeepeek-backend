using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Moderation.Application.Features.Import.IngestImportFile;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Infrastructure.Consumers;
using CoffeeShop.Moderation.Persistence.Configuration;
using FluentAssertions;

namespace CoffeePeek.ModerationService.Tests;

public class ArchitectureBoundaryTests
{
    [Fact]
    public void Domain_DoesNotDependOnOuterLayersOrWireContracts()
    {
        var references = typeof(ModerationShop).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        references.Should().NotContain(x => x != null &&
            (x.Contains("Application") || x.Contains("Infrastructure") || x.Contains("Persistence")
             || x == "CoffeePeek.Contract" || x.StartsWith("Microsoft.EntityFrameworkCore")));
    }

    [Fact]
    public void Application_OwnsQueriesAndInputParsing_WithoutPersistenceDependency()
    {
        var application = typeof(ImportFileParser).Assembly;
        Assert.Same(application, typeof(IQueryModerationShopRepository).Assembly);
        Assert.Same(application, typeof(IAdminModerationStatsQueryRepository).Assembly);
        application.GetReferencedAssemblies().Select(x => x.Name).Should().NotContain([
            typeof(ModerationDbContext).Assembly.GetName().Name,
            typeof(MenuParsedEventHandler).Assembly.GetName().Name]);
    }
}
