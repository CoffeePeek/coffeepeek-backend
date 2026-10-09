using CoffeePeek.Shops.Domain.Aggregates.AppDistributionAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public class AppDistributionReadOnlySettingsTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task MissingSettings_ReadReturnsDefaultsWithoutCreatingRow()
    {
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        await using (var db = new ShopsDbContext(options))
        {
            var settings = await new AppDistributionRepository(db).GetSettingsOrDefaultAsync(_ct);

            settings.Id.Should().Be(AppDistributionSettings.SingletonId);
            db.ChangeTracker.Entries<AppDistributionSettings>().Should().BeEmpty();
            await db.SaveChangesAsync(_ct);
        }

        await using (var db = new ShopsDbContext(options))
            (await db.AppDistributionSettings.CountAsync(_ct)).Should().Be(0);
    }

    [Fact]
    public async Task ExistingSettings_ReadDoesNotTrackIt()
    {
        var options = new DbContextOptionsBuilder<ShopsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        await using (var db = new ShopsDbContext(options))
        {
            db.AppDistributionSettings.Add(AppDistributionSettings.CreateDefault());
            await db.SaveChangesAsync(_ct);
        }

        await using (var db = new ShopsDbContext(options))
        {
            var settings = await new AppDistributionRepository(db).GetSettingsOrDefaultAsync(_ct);

            settings.Id.Should().Be(AppDistributionSettings.SingletonId);
            db.ChangeTracker.Entries<AppDistributionSettings>().Should().BeEmpty();
        }
    }
}
