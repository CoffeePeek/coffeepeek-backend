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

        entity.HasIndex(mr => mr.ShopId);
        entity.HasIndex(mr => mr.UserId);
        entity.HasIndex(mr => mr.ModeratedBy);
        entity.HasIndex(mr => new { mr.CheckInId, mr.ContentRevision }).IsUnique();
        // Queue pages filter by status and order by newest; the composite also serves status-only filters.
        entity.HasIndex(mr => new { mr.ModerationStatus, mr.CreatedAtUtc });

        entity.Property(mr => mr.UserName).HasMaxLength(30);
        entity.Property(mr => mr.DrinkSlug).HasMaxLength(50);
        entity.Property(mr => mr.CustomDrinkName).HasMaxLength(100);
        entity.Property(mr => mr.DrinkNameRu).HasMaxLength(100);
        entity.Property(mr => mr.DrinkNameEn).HasMaxLength(100);
        entity.Property(mr => mr.Comment).HasMaxLength(BusinessConstants.MaxCheckInTextLength);
        entity.ToTable("ModerationCheckIns", table => table.HasCheckConstraint("CK_ModerationCheckIns_CanonicalSnapshot",
            "\"CheckInId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ContentRevision\" > 0 AND " +
            "\"Comment\" IS NOT NULL AND length(btrim(\"Comment\")) <= 1000 AND " +
            "((\"Rating_Place\" IS NULL AND \"Rating_Service\" IS NULL AND \"Rating_Coffee\" IS NULL AND \"Rating_AverageRating\" IS NULL) OR " +
            "(\"Rating_Place\" IS NOT NULL AND \"Rating_Service\" IS NOT NULL AND \"Rating_Coffee\" IS NOT NULL AND \"Rating_AverageRating\" IS NOT NULL AND " +
            "\"Rating_Place\" BETWEEN 1 AND 5 AND \"Rating_Service\" BETWEEN 1 AND 5 AND \"Rating_Coffee\" BETWEEN 1 AND 5))"));
        entity.Property(mr => mr.RejectedReason).HasMaxLength(BusinessConstants.MaxRejectReasonCommentLength);

        entity.HasMany(mr => mr.ReviewPhotos).WithOne().HasForeignKey("ModerationCheckInId");
        entity.OwnsOne<Rating>(r => r.Rating);
        entity.Navigation(mr => mr.Rating).IsRequired(false);
    }
}
