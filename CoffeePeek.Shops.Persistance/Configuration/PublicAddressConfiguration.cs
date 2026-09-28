using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using CoffeePeek.Shops.Domain.Aggregates.PublicAddressAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public sealed class CoffeeShopPublicAddressConfiguration : IEntityTypeConfiguration<CoffeeShopPublicAddress>
{
    public void Configure(EntityTypeBuilder<CoffeeShopPublicAddress> builder)
    {
        PublicAddressConfiguration.Configure(builder, "CoffeeShopPublicAddresses");
        builder.HasOne<CoffeeShop>().WithMany().HasForeignKey(a => a.OwnerId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RoasterPublicAddressConfiguration : IEntityTypeConfiguration<RoasterPublicAddress>
{
    public void Configure(EntityTypeBuilder<RoasterPublicAddress> builder)
    {
        PublicAddressConfiguration.Configure(builder, "RoasterPublicAddresses");
        builder.HasOne<Roaster>().WithMany().HasForeignKey(a => a.OwnerId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CityPublicAddressConfiguration : IEntityTypeConfiguration<CityPublicAddress>
{
    public void Configure(EntityTypeBuilder<CityPublicAddress> builder)
    {
        PublicAddressConfiguration.Configure(builder, "CityPublicAddresses");
        builder.HasOne<City>().WithMany().HasForeignKey(a => a.OwnerId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CoffeeZonePublicAddressConfiguration : IEntityTypeConfiguration<CoffeeZonePublicAddress>
{
    public void Configure(EntityTypeBuilder<CoffeeZonePublicAddress> builder)
    {
        PublicAddressConfiguration.Configure(builder, "CoffeeZonePublicAddresses");
        builder.HasOne<CoffeeZone>().WithMany().HasForeignKey(a => a.OwnerId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal static class PublicAddressConfiguration
{
    public static void Configure<T>(EntityTypeBuilder<T> builder, string table) where T : PublicAddress
    {
        // Separate typed registries, not an EF inheritance hierarchy or a polymorphic foreign key.
        builder.HasBaseType((Type?)null);
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Slug).IsRequired().HasMaxLength(PublicSlug.MaxLength).UseCollation("C");
        builder.Property(a => a.Revision).IsConcurrencyToken();
        builder.HasIndex(a => a.Slug).IsUnique();
        // At most one canonical address. Phase 2's transaction coordinator guarantees at least one.
        builder.HasIndex(a => a.OwnerId).IsUnique().HasFilter("\"State\" = 1");
        builder.HasIndex(a => new { a.OwnerId, a.State });
        builder.ToTable(table, t =>
        {
            t.HasCheckConstraint($"CK_{table}_Slug", "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100");
            t.HasCheckConstraint($"CK_{table}_Lifecycle", "(\"State\" IN (1, 2) AND \"OwnerId\" IS NOT NULL) OR (\"State\" = 3 AND \"OwnerId\" IS NULL)");
            t.HasCheckConstraint($"CK_{table}_Revision", "\"Revision\" > 0");
            t.HasCheckConstraint($"CK_{table}_Timestamps", "\"UpdatedAtUtc\" >= \"CreatedAtUtc\"");
        });
    }
}
