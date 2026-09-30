using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Domain.Aggregates.PublicAddressAggregate;
using CoffeePeek.Shops.Persistance.Configuration;
using CoffeePeek.Shops.Persistance.PublicAddresses;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;

public sealed class PublicAddressPersistenceTests
{
    [Theory]
    [InlineData(typeof(CoffeeShopPublicAddress), "CoffeeShopPublicAddresses", typeof(CoffeeShop))]
    [InlineData(typeof(RoasterPublicAddress), "RoasterPublicAddresses", typeof(Roaster))]
    [InlineData(typeof(CityPublicAddress), "CityPublicAddresses", typeof(City))]
    [InlineData(typeof(CoffeeZonePublicAddress), "CoffeeZonePublicAddresses", typeof(CoffeeZone))]
    [InlineData(typeof(CoffeeBeanPublicAddress), "CoffeeBeanPublicAddresses", typeof(CoffeeBean))]
    [InlineData(typeof(EquipmentPublicAddress), "EquipmentPublicAddresses", typeof(Equipment))]
    [InlineData(typeof(BrewMethodPublicAddress), "BrewMethodPublicAddresses", typeof(CoffeePeek.Shops.Domain.Aggregates.BrewMethods.BrewMethod))]
    [InlineData(typeof(ShopTagPublicAddress), "ShopTagPublicAddresses", typeof(CoffeePeek.Shops.Domain.Aggregates.ShopTagAggregate.ShopTag))]
    public void Model_ReservesHistoryAndRequiresExplicitRetirementBeforeDelete(Type type, string table, Type principal)
    {
        using var db = CreateNpgsqlDb();
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
        var foreignKey = entity.GetForeignKeys().Should().ContainSingle().Which;
        foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
        foreignKey.PrincipalEntityType.ClrType.Should().Be(principal);
        entity.GetCheckConstraints().Select(c => c.Name).Should().BeEquivalentTo(
            $"CK_{table}_Slug", $"CK_{table}_Lifecycle", $"CK_{table}_Revision", $"CK_{table}_Timestamps");
    }

    [Fact]
    public void Model_AuditKindCheckCoversEveryAddressKind()
    {
        using var db = CreateNpgsqlDb();
        var audit = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PublicAddressAudit))!;
        var check = audit.GetCheckConstraints().Single(c => c.Name == "CK_PublicAddressAudit_Kind");
        check.Sql.Should().Be("\"Kind\" IN (1, 2, 3, 4, 5, 6, 7, 8)");

        var values = Enum.GetValues<PublicAddressKind>().Select(k => (int)k).ToArray();
        values.Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
        foreach (var value in values) check.Sql.Should().Contain(value.ToString());
    }

    private static ShopsDbContext CreateNpgsqlDb() => new(new DbContextOptionsBuilder<ShopsDbContext>()
        .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options);
}
