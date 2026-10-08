using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public class RoasterConfiguration : IEntityTypeConfiguration<Roaster>
{
    public void Configure(EntityTypeBuilder<Roaster> builder)
    {
        builder.HasKey(r => r.Id);
        // Backstop for the read-then-insert idempotency check in CreateRoasterFromModerationService.
        builder.HasIndex(r => r.ModerationId)
            .IsUnique()
            .HasFilter("\"ModerationId\" IS NOT NULL");
        builder.Property(r => r.Name).HasMaxLength(BusinessConstants.MaxRoasterNameLength);
        builder.Property(r => r.About).HasMaxLength(BusinessConstants.MaxRoasterAboutLength);

        var navigationPhotos = builder.Metadata.FindNavigation(nameof(Roaster.Photos));
        navigationPhotos?.SetPropertyAccessMode(PropertyAccessMode.Field);

        // Required + cascade so ReplacePhotos deletes the old rows instead of orphaning
        // them with RoasterId = NULL.
        builder.HasMany(r => r.Photos)
            .WithOne()
            .HasForeignKey("RoasterId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.OwnsOne(r => r.Contact, contact =>
        {
            contact
                .Property(c => c.InstagramLink)
                .HasColumnName(nameof(RoasterContact.InstagramLink))
                .HasMaxLength(BusinessConstants.MaxRoasterContactInstagramLinkLength);

            contact
                .Property(c => c.SiteLink)
                .HasColumnName(nameof(RoasterContact.SiteLink))
                .HasMaxLength(BusinessConstants.MaxRoasterContactSiteLinkLength);
        });

        builder.OwnsOne(r => r.Location, location =>
        {
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
            location.HasOne<City>()
                .WithMany()
                .HasForeignKey(l => l.CityId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_Roasters_Cities_CityId");
        });
    }
}
