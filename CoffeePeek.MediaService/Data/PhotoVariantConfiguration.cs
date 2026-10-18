using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.MediaService.Data;

public sealed class PhotoVariantConfiguration : IEntityTypeConfiguration<PhotoVariant>
{
    public void Configure(EntityTypeBuilder<PhotoVariant> entity)
    {
        entity.HasKey(x => x.Id);
        entity.Property(x => x.SourceVersion).HasMaxLength(64);
        entity.Property(x => x.Purpose).HasMaxLength(20);
        entity.Property(x => x.Format).HasMaxLength(10);
        entity.Property(x => x.ContentType).HasMaxLength(40);
        entity.Property(x => x.ObjectKey).HasMaxLength(255);
        entity.Property(x => x.Digest).HasMaxLength(64);
        entity.HasIndex(x => new { x.PhotoId, x.SourceVersion, x.RecipeVersion, x.Purpose, x.Format }).IsUnique();
        entity.HasIndex(x => x.ObjectKey).IsUnique();
        entity.HasOne<PhotoVariantJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        entity.ToTable("PhotoVariants", t => t.HasCheckConstraint("CK_PhotoVariants_Dimensions",
            "\"Width\" > 0 AND \"Height\" > 0 AND \"SizeBytes\" > 0"));
    }
}
