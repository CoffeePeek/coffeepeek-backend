using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public class CoffeeShopConfiguration : IEntityTypeConfiguration<CoffeeShop>
{
    public void Configure(EntityTypeBuilder<CoffeeShop> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(x => x.Name).HasMaxLength(BusinessConstants.MaxCoffeeShopNameLength);
        builder.Property(x => x.Description).HasMaxLength(BusinessConstants.MaxCoffeeShopDescriptionLength);
        builder.Property(x => x.Type);
        builder.Property(x => x.DataCompletenessScore)
            .HasDefaultValue((short)0);
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_Shops_DataCompletenessScore_Range",
            "\"DataCompletenessScore\" BETWEEN 0 AND 100"));
        builder.HasIndex(s => s.OwnerUserId);
        builder.HasIndex(s => s.ImportedFromFileAt);
        // Backstop for the read-then-insert idempotency check in CreateShopFrom{Moderation,Import}Service:
        // a redelivered or concurrently processed publish event must not create a second catalog shop.
        builder.HasIndex(s => s.ModerationId)
            .IsUnique()
            .HasFilter("\"ModerationId\" IS NOT NULL")
            .HasDatabaseName("IX_Shops_ModerationId");
        builder.HasIndex(s => new { s.Status, s.DataCompletenessScore, s.Name })
            .IsDescending(false, true, false)
            .HasDatabaseName("IX_Shops_Status_DataCompletenessScore_Name");

        builder.HasIndex(s => s.Name)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops")
            .HasDatabaseName("IX_Shops_Name_GIN");

        var navigationPhotos = builder.Metadata.FindNavigation(nameof(CoffeeShop.ShopPhotos));
        navigationPhotos?.SetPropertyAccessMode(PropertyAccessMode.Field);

        var navigationSchedules = builder.Metadata.FindNavigation(nameof(CoffeeShop.Schedules));
        navigationSchedules?.SetPropertyAccessMode(PropertyAccessMode.Field);

        var navigationShopTags = builder.Metadata.FindNavigation(nameof(CoffeeShop.ShopTags));
        navigationShopTags?.SetPropertyAccessMode(PropertyAccessMode.Field);

        #region Value-objects

        builder.OwnsOne(e => e.Contact, contact =>
        {
            contact
                .Property(sc => sc.PhoneNumber)
                .HasColumnName(nameof(ShopContact.PhoneNumber))
                .HasMaxLength(BusinessConstants.MaxShopContactPhoneNumberLength);
            
            contact
                .Property(sc => sc.Email)
                .HasColumnName(nameof(ShopContact.Email))
                .HasMaxLength(BusinessConstants.MaxShopContactEmailLength);
            
            contact
                .Property(sc => sc.SiteLink)
                .HasColumnName(nameof(ShopContact.SiteLink))
                .HasMaxLength(BusinessConstants.MaxShopContactSiteLinkLength);
            
            contact
                .Property(sc => sc.InstagramLink)
                .HasColumnName(nameof(ShopContact.InstagramLink))
                .HasMaxLength(BusinessConstants.MaxShopContactInstagramLinkLength);
        });

        builder.OwnsOne(e => e.Location, location =>
        {
            location.HasIndex(l => new { l.Latitude, l.Longitude });
            // City filters in search, zones and admin lists seq-scanned Shops.
            location.HasIndex(l => l.CityId);

            location.HasIndex(l => l.Address)
                .HasMethod("gin")
                .HasOperators("gin_trgm_ops")
                .HasDatabaseName("IX_Shops_Address_GIN");

            location
                .Property(l => l.Address)
                .HasColumnName(nameof(Location.Address))
                .HasMaxLength(BusinessConstants.MaxLocationAddressLength);
            location
                .Property(l => l.Latitude)
                .HasColumnName(nameof(Location.Latitude))
                .HasPrecision(BusinessConstants.MaxLocationPrecision, BusinessConstants.MaxLocationScale);
            location
                .Property(l => l.Longitude)
                .HasColumnName(nameof(Location.Longitude))
                .HasPrecision(BusinessConstants.MaxLocationPrecision, BusinessConstants.MaxLocationScale);
            location
                .Property(l => l.IsAddressValidated)
                .HasColumnName(nameof(Location.IsAddressValidated));
            location
                .Property(l => l.CityId)
                .HasColumnName(nameof(Location.CityId));
        });

        builder.OwnsMany(e => e.Schedules, schedules =>
        {
            schedules.Property(s => s.DayOfWeek).HasColumnName(nameof(ShopSchedule.DayOfWeek));
            schedules.Property(s => s.IsClosed).HasColumnName(nameof(ShopSchedule.IsClosed));
            
            schedules.OwnsMany(sc => sc.Intervals, intervals =>
            {
                intervals.Property(i => i.OpenTime).HasColumnName(nameof(ShopScheduleInterval.OpenTime)).IsRequired();
                intervals.Property(i => i.CloseTime).HasColumnName(nameof(ShopScheduleInterval.CloseTime)).IsRequired();
            });
        });

        #endregion

        #region many-to-many

        builder
            .HasMany(u => u.BrewMethods)
            .WithMany(r => r.CoffeeShops)
            .UsingEntity<Dictionary<string, object>>(
                "CoffeeShopBrewMethods",
                j => j.HasOne<BrewMethod>().WithMany().HasForeignKey("BrewMethodId"),
                j => j.HasOne<CoffeeShop>().WithMany().HasForeignKey("CoffeeShopId")
                );

        builder
            .HasMany(u => u.Roasters)
            .WithMany(r => r.CoffeeShops)
            .UsingEntity<Dictionary<string, object>>(
                "CoffeeShopRoasters",
                j => j.HasOne<Roaster>().WithMany().HasForeignKey("RoasterId"),
                j => j.HasOne<CoffeeShop>().WithMany().HasForeignKey("CoffeeShopId")
                );

        builder
            .HasMany(u => u.CoffeeBeans)
            .WithMany(r => r.CoffeeShops)
            .UsingEntity<Dictionary<string, object>>(
                "CoffeeShopCoffeeBeans",
                j => j.HasOne<CoffeeBean>().WithMany().HasForeignKey("CoffeeBeanId"),
                j => j.HasOne<CoffeeShop>().WithMany().HasForeignKey("CoffeeShopId")
                );

        builder
            .HasMany(u => u.Equipments)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "CoffeeShopEquipments",
                j => j.HasOne<Equipment>().WithMany().HasForeignKey("EquipmentId"),
                j => j.HasOne<CoffeeShop>().WithMany().HasForeignKey("CoffeeShopId")
            );

        #endregion
    }
}
