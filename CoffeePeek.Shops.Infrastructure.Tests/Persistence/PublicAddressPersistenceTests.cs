using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.PublicAddressAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class PublicAddressPersistenceTests
{
    [Theory]
    [InlineData(typeof(CoffeeShopPublicAddress), "CoffeeShopPublicAddresses")]
    [InlineData(typeof(RoasterPublicAddress), "RoasterPublicAddresses")]
    public void Model_ReservesHistoryAndRequiresExplicitRetirementBeforeDelete(Type type, string table)
    {
        using var db = new ShopsDbContext(new DbContextOptionsBuilder<ShopsDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options);
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(type)!;
        entity.GetTableName().Should().Be(table);
        entity.BaseType.Should().BeNull();
        entity.FindProperty(nameof(PublicAddress.Slug))!.GetMaxLength().Should().Be(100);
        entity.FindProperty(nameof(PublicAddress.Slug))!.GetCollation().Should().Be("C");
        entity.FindProperty(nameof(PublicAddress.Revision))!.IsConcurrencyToken.Should().BeTrue();
        var slug = entity.GetIndexes().Single(i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(PublicAddress.Slug)]));
        slug.IsUnique.Should().BeTrue();
        slug.GetFilter().Should().BeNull();
        var canonical = entity.GetIndexes().Single(i => i.IsUnique && i != slug);
        canonical.Properties.Should().ContainSingle(p => p.Name == nameof(PublicAddress.OwnerId));
        canonical.GetFilter().Should().Be("\"State\" = 1");
        entity.GetForeignKeys().Should().ContainSingle().Which.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
        entity.GetCheckConstraints().Select(c => c.Name).Should().BeEquivalentTo(
            $"CK_{table}_Slug", $"CK_{table}_Lifecycle", $"CK_{table}_Revision", $"CK_{table}_Timestamps");
    }
}
