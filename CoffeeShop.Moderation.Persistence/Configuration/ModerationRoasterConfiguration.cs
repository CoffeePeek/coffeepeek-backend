using CoffeePeek.Moderation.Domain;
using CoffeePeek.Moderation.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeShop.Moderation.Persistence.Configuration;

public class ModerationRoasterConfiguration : IEntityTypeConfiguration<ModerationRoaster>
{
    public void Configure(EntityTypeBuilder<ModerationRoaster> entity)
    {
        entity.UsePropertyAccessMode(PropertyAccessMode.Field);

        entity.HasKey(e => e.Id);

        // Optimistic concurrency on PostgreSQL xmin: two moderators deciding the same item at once must not both win.
        entity.Property<uint>("Version").IsRowVersion();
        entity.Property(e => e.Name).HasMaxLength(BusinessConstants.MaxRoasterNameLength);
        entity.Property(e => e.About).HasMaxLength(BusinessConstants.MaxRoasterAboutLength);
        entity.Property(e => e.RejectedReason).HasMaxLength(BusinessConstants.MaxRejectReasonCommentLength);
        entity.HasIndex(e => e.UserId);
        // Queue pages filter by status and order by newest; the composite also serves status-only filters.
        entity.HasIndex(e => new { e.ModerationStatus, e.CreatedAtUtc });

        entity.OwnsOne(e => e.Contact, contact =>
        {
            contact.Property(c => c.InstagramLink).HasMaxLength(BusinessConstants.MaxShopContactInstagramLinkLength);
            contact.Property(c => c.SiteLink).HasMaxLength(BusinessConstants.MaxShopContactSiteLinkLength);
        });

        entity.OwnsOne(e => e.Location, location => { location.HasIndex(l => new { l.Latitude, l.Longitude }); });
    }
}
