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
            table.HasCheckConstraint("CK_CoffeeZones_Color", "\"Color\" ~ '^#[0-9A-F]{6}$'");
        });

        builder.HasKey(z => z.Id);
        builder.Property(z => z.Name).HasMaxLength(BusinessConstants.MaxCoffeeZoneNameLength).IsRequired();
        builder.Property(z => z.Description).HasMaxLength(BusinessConstants.MaxCoffeeZoneDescriptionLength);
        builder.Property(z => z.Color).HasMaxLength(7).HasDefaultValue(CoffeeZone.DefaultColor).IsRequired();
        builder.Property(z => z.CenterLatitude).HasPrecision(BusinessConstants.MaxLocationPrecision, BusinessConstants.MaxLocationScale);
        builder.Property(z => z.CenterLongitude).HasPrecision(BusinessConstants.MaxLocationPrecision, BusinessConstants.MaxLocationScale);
        builder.ComplexCollection(z => z.Polygon, polygon => polygon.ToJson())
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(z => new { z.CityId, z.Status });
        builder.HasOne<City>().WithMany().HasForeignKey(z => z.CityId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_CoffeeZones_Cities_CityId");
    }
}
