using CoffeePeek.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModerationShop = CoffeePeek.Moderation.Domain.Aggregates.ModerationShop;

namespace CoffeeShop.Moderation.Persistence.Configuration;

public class ModerationShopConfiguration : IEntityTypeConfiguration<ModerationShop>
{
    public void Configure(EntityTypeBuilder<ModerationShop> entity)
    {
        entity.UsePropertyAccessMode(PropertyAccessMode.Field);

        entity.HasKey(e => e.Id);

        // Optimistic concurrency on PostgreSQL xmin: two moderators deciding the same item at once must not both win.
        entity.Property<uint>("Version").IsRowVersion();
        entity.Property(e => e.Name).HasMaxLength(BusinessConstants.MaxShopNameLength);
        entity.Property(e => e.Description).HasMaxLength(BusinessConstants.MaxShopDescriptionLength);
        entity.Property(e => e.RejectedReason).HasMaxLength(BusinessConstants.MaxRejectReasonCommentLength);
        entity.HasIndex(e => e.UserId);
        // Queue pages filter by status and order by newest; the composite also serves status-only filters.
        entity.HasIndex(e => new { e.ModerationStatus, e.CreatedAtUtc });
        entity.HasIndex(e => e.ShopId);
        entity.HasIndex(e => e.CityId);
        entity.HasIndex(e => e.CoffeeFocus);

        entity.OwnsOne(e => e.Contact, contact =>
        {
            contact.Property(sc => sc.PhoneNumber).HasMaxLength(BusinessConstants.MaxShopContactPhoneNumberLength);
            contact.Property(sc => sc.Email).HasMaxLength(BusinessConstants.MaxShopContactEmailLength);
            contact.Property(sc => sc.SiteLink).HasMaxLength(BusinessConstants.MaxShopContactSiteLinkLength);
            contact.Property(sc => sc.InstagramLink)
                .HasMaxLength(BusinessConstants.MaxShopContactInstagramLinkLength);
        });

        entity.OwnsOne(e => e.Location, location => { location.HasIndex(l => new { l.Latitude, l.Longitude }); });

        entity.OwnsMany(e => e.Schedules, schedules =>
        {
            schedules.OwnsMany(sc => sc.Intervals, intervals =>
            {
                intervals.Property(i => i.OpenTime).IsRequired();
                intervals.Property(i => i.CloseTime).IsRequired();
            });
        });

        entity.HasMany(e => e.ShopPhotos)
            .WithOne()
            .HasForeignKey(x => x.ModerationShopId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(e => e.ModerationShopEquipments)
            .WithOne(eq => eq.ModerationShop)
            .HasForeignKey(eq => eq.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(e => e.ModerationCoffeeBeanShops)
            .WithOne(cb => cb.ModerationShop)
            .HasForeignKey(cb => cb.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(e => e.ModerationRoasterShops)
            .WithOne(rs => rs.ModerationShop)
            .HasForeignKey(rs => rs.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasMany(e => e.ModerationShopBrewMethods)
            .WithOne(bm => bm.ModerationShop)
            .HasForeignKey(bm => bm.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.OwnsOne(e => e.Menu, menu =>
        {
            menu.ToJson();
            menu.OwnsMany(m => m.Items);
            menu.OwnsMany(m => m.Photos);
            menu.OwnsMany(m => m.Unmatched);
        });
        entity.Navigation(e => e.Menu).UsePropertyAccessMode(PropertyAccessMode.Property);
    }
}
