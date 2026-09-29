using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.PublicAddresses;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

// D-05 (live gate item G-S10b): an out-of-band writer that takes a slug while a write-mode backfill page is in
// flight must produce 409, as in ChangeAsync and Account's ProfileAddressService, not a raw DbUpdateException (500).
// InMemory proves only the mapping, its filter and that dry-run never saves. The owned transaction that rolls the
// page back is bypassed under InMemory and is proven live by ShopsBackfillRehearsalTests.
public sealed class PublicAddressBackfillConflictTests
{
    [Theory]
    [InlineData(PublicAddressKind.CoffeeShop, "IX_CoffeeShopPublicAddresses_Slug")]
    [InlineData(PublicAddressKind.Roaster, "IX_RoasterPublicAddresses_OwnerId")]
    [InlineData(PublicAddressKind.City, "IX_CityPublicAddresses_Slug")]
    [InlineData(PublicAddressKind.CoffeeZone, "IX_CoffeeZonePublicAddresses_Slug")]
    [InlineData(PublicAddressKind.City, "IX_PublicAddressAudit_Kind_OwnerId_Revision")]
    public async Task WriteBackfill_AddressUniqueViolation_Returns409(PublicAddressKind kind, string constraint)
    {
        var ct = TestContext.Current.CancellationToken;
        var (databaseName, root) = await SeedUninitializedOwnerAsync(kind, ct);
        await using var db = CreateDb(enabled: true, databaseName, root,
            new ThrowingSaveInterceptor(PostgresErrorCodes.UniqueViolation, constraint));

        var action = () => new PublicAddressService(db).BackfillAsync(kind, null, 100, dryRun: false, Guid.NewGuid(), ct);

        var thrown = (await action.Should().ThrowExactlyAsync<PublicAddressException>()).Which;
        thrown.StatusCode.Should().Be(409);
        thrown.Message.Should().Be("Address is reserved or changed concurrently.");
    }

    [Theory]
    [InlineData(PublicAddressKind.CoffeeShop, PostgresErrorCodes.UniqueViolation, "IX_Shops_ModerationId")]
    [InlineData(PublicAddressKind.City, PostgresErrorCodes.ForeignKeyViolation, "FK_CityPublicAddresses_Cities_OwnerId")]
    public async Task WriteBackfill_NonAddressDatabaseError_IsNotMappedTo409(PublicAddressKind kind, string sqlState,
        string constraint)
    {
        var ct = TestContext.Current.CancellationToken;
        var (databaseName, root) = await SeedUninitializedOwnerAsync(kind, ct);
        await using var db = CreateDb(enabled: true, databaseName, root, new ThrowingSaveInterceptor(sqlState, constraint));

        var action = () => new PublicAddressService(db).BackfillAsync(kind, null, 100, dryRun: false, Guid.NewGuid(), ct);

        // The mapping is limited to address constraints: anything else keeps propagating unchanged.
        var thrown = (await action.Should().ThrowExactlyAsync<DbUpdateException>()).Which;
        thrown.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(sqlState);
    }

    [Fact]
    public async Task DryRunBackfill_NeverSaves()
    {
        var ct = TestContext.Current.CancellationToken;
        var (databaseName, root) = await SeedUninitializedOwnerAsync(PublicAddressKind.City, ct);
        // Any save on this context throws, so a dry run that returns proves it performed no save.
        await using var db = CreateDb(enabled: true, databaseName, root,
            new ThrowingSaveInterceptor(PostgresErrorCodes.UniqueViolation, "IX_CityPublicAddresses_Slug"));

        var result = await new PublicAddressService(db).BackfillAsync(PublicAddressKind.City, null, 100, dryRun: true,
            Guid.NewGuid(), ct);

        result.DryRun.Should().BeTrue();
        var item = result.Items.Should().ContainSingle().Which;
        item.AlreadyInitialized.Should().BeFalse();
        item.Slug.Should().NotBeNullOrWhiteSpace();
        result.NextCursor.Should().BeNull();
    }

    // One owner without an address, saved through a plain flag-off context (no allocation, no interceptor).
    private static async Task<(string DatabaseName, InMemoryDatabaseRoot Root)> SeedUninitializedOwnerAsync(
        PublicAddressKind kind, CancellationToken ct)
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = Guid.NewGuid().ToString();
        await using var seed = CreateDb(enabled: false, databaseName, root);
        switch (kind)
        {
            case PublicAddressKind.CoffeeShop:
                seed.Shops.Add(new CoffeeShop(Guid.NewGuid(), "Conflict Coffee", null, PriceRange.Moderate, Guid.NewGuid()));
                break;
            case PublicAddressKind.Roaster:
                seed.Roasters.Add(new Roaster("Conflict Roaster"));
                break;
            case PublicAddressKind.City:
                seed.Cities.Add(new City("Гродно"));
                break;
            case PublicAddressKind.CoffeeZone:
                var city = new City("Брест");
                var zone = new CoffeeZone(city.Id, "Conflict Zone", null, CoffeeZonePoint.Circle(53.9m, 27.56m, 400));
                zone.Publish();
                seed.Cities.Add(city);
                seed.CoffeeZones.Add(zone);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        await seed.SaveChangesAsync(ct);
        return (databaseName, root);
    }

    private static ShopsDbContext CreateDb(bool enabled, string databaseName, InMemoryDatabaseRoot root,
        SaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<ShopsDbContext>().UseInMemoryDatabase(databaseName, root);
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return new ShopsDbContext(builder.Options, Options.Create(new PublicAddressOptions { Enabled = enabled }));
    }

    // Simulates the database rejecting the page's INSERTs, as PostgreSQL does when another session committed the slug.
    private sealed class ThrowingSaveInterceptor(string sqlState, string constraint) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("simulated",
                new PostgresException("simulated", "ERROR", "ERROR", sqlState, constraintName: constraint));
    }
}
