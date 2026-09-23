using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeZoneAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public sealed class CoffeeZoneConfiguration : IEntityTypeConfiguration<CoffeeZone>
{
    public void Configure(EntityTypeBuilder<CoffeeZone> builder)
    {
        builder.ToTable("CoffeeZones", table =>
        {
            table.HasCheckConstraint("CK_CoffeeZones_Latitude", "\"CenterLatitude\" BETWEEN -90 AND 90");
            table.HasCheckConstraint("CK_CoffeeZones_Longitude", "\"CenterLongitude\" BETWEEN -180 AND 180");
            table.HasCheckConstraint(
                "CK_CoffeeZones_RadiusMeters",
                $"\"RadiusMeters\" BETWEEN {BusinessConstants.MinCoffeeZoneRadiusMeters} AND {BusinessConstants.MaxCoffeeZoneRadiusMeters}");
            table.HasCheckConstraint("CK_CoffeeZones_Status", "\"Status\" BETWEEN 0 AND 2");
        });

        builder.HasKey(z => z.Id);
        builder.Property(z => z.Name).HasMaxLength(BusinessConstants.MaxCoffeeZoneNameLength).IsRequired();
        builder.Property(z => z.Description).HasMaxLength(BusinessConstants.MaxCoffeeZoneDescriptionLength);
        builder.Property(z => z.CenterLatitude).HasPrecision(BusinessConstants.MaxLocationPrecision, BusinessConstants.MaxLocationScale);
        builder.Property(z => z.CenterLongitude).HasPrecision(BusinessConstants.MaxLocationPrecision, BusinessConstants.MaxLocationScale);
        builder.HasIndex(z => new { z.CityId, z.Status });
        builder.HasOne<City>().WithMany().HasForeignKey(z => z.CityId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CoffeeZoneMembershipOverrideConfiguration : IEntityTypeConfiguration<CoffeeZoneMembershipOverride>
{
    public void Configure(EntityTypeBuilder<CoffeeZoneMembershipOverride> builder)
    {
        builder.ToTable("CoffeeZoneMembershipOverrides", table =>
            table.HasCheckConstraint("CK_CoffeeZoneMembershipOverrides_Kind", "\"Kind\" BETWEEN 0 AND 2"));
        builder.HasKey(x => new { x.ZoneId, x.ShopId });
        builder.HasOne<CoffeeZone>().WithMany().HasForeignKey(x => x.ZoneId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CoffeeShop>().WithMany().HasForeignKey(x => x.ShopId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.ShopId);
        builder.HasIndex(x => x.ShopId)
            .IsUnique()
            .HasFilter("\"Kind\" = 2")
            .HasDatabaseName("UX_CoffeeZoneMembershipOverrides_PrimaryShop");
    }
}
