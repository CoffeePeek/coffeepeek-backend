using CoffeePeek.Moderation.Domain;
using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModerationReview = CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate.ModerationReview;

namespace CoffeeShop.Moderation.Persistence.Configuration;

public class ModerationReviewConfiguration : IEntityTypeConfiguration<ModerationReview>
{
    public void Configure(EntityTypeBuilder<ModerationReview> entity)
    {
        entity.UsePropertyAccessMode(PropertyAccessMode.Field);
        entity.HasKey(mr => mr.Id);

        // Optimistic concurrency on PostgreSQL xmin: two moderators deciding the same item at once must not both win.
        entity.Property<uint>("Version").IsRowVersion();
        entity.HasOne(mr => mr.ModerationShop)
            .WithMany()
            .HasForeignKey(mr => mr.ModerationShopId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);
            
        entity.HasIndex(mr => mr.ShopId);
        entity.HasIndex(mr => mr.UserId);
        entity.HasIndex(mr => mr.ModeratedBy);
        entity.HasIndex(mr => mr.ModerationStatus);

        entity.Property(mr => mr.UserName).HasMaxLength(30);
        entity.Property(mr => mr.Header)
            .IsRequired(false)
            .HasMaxLength(BusinessConstants.MaxReviewHeaderLength);
        entity.Property(mr => mr.Comment).HasMaxLength(BusinessConstants.MaxReviewCommentLength);
        entity.Property(mr => mr.RejectedReason).HasMaxLength(BusinessConstants.MaxRejectReasonCommentLength);
            
        entity.OwnsOne<Rating>(r => r.Rating);
    }
}
