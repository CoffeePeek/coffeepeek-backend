using CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeeShop.Moderation.Persistence.Configuration;

public class ShopChangeRequestConfiguration : IEntityTypeConfiguration<ShopChangeRequest>
{
    public void Configure(EntityTypeBuilder<ShopChangeRequest> entity)
    {
        entity.HasKey(x => x.Id);

        // Optimistic concurrency on PostgreSQL xmin: two moderators deciding the same item at once must not both win.
        entity.Property<uint>("Version").IsRowVersion();
        entity.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        entity.Property(x => x.RejectionReason).HasMaxLength(500);
        entity.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        entity.HasIndex(x => x.ShopId);
        entity.HasIndex(x => x.SubmittedByUserId);
        entity.HasIndex(x => x.Section);
    }
}
