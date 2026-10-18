using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.MediaService.Data;

public sealed class PhotoVariantJobConfiguration : IEntityTypeConfiguration<PhotoVariantJob>
{
    public void Configure(EntityTypeBuilder<PhotoVariantJob> entity)
    {
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.SourceKey).HasMaxLength(255);
        entity.Property(x => x.SourceVersion).HasMaxLength(64);
        entity.Property(x => x.SourceDigest).HasMaxLength(64);
        entity.Property(x => x.FailureCode).HasMaxLength(40);
        entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        entity.Property(x => x.Priority).HasDefaultValue(0);
        entity.HasIndex(x => new { x.PhotoId, x.SourceVersion, x.RecipeVersion }).IsUnique();
        entity.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
        entity.HasIndex(x => new { x.Status, x.LeaseUntilUtc });
        entity.HasIndex(x => x.CheckInId);
        entity.HasIndex(x => new { x.Priority, x.Status, x.NextAttemptAtUtc });
        entity.HasOne<PhotoBackfillRun>().WithMany().HasForeignKey(x => x.BackfillRunId).OnDelete(DeleteBehavior.Restrict);
        entity.ToTable("PhotoVariantJobs", table => table.HasCheckConstraint("CK_PhotoVariantJobs_Priority", "(\"Priority\" = 0 AND \"BackfillRunId\" IS NULL) OR (\"Priority\" = 1 AND \"BackfillRunId\" IS NOT NULL)"));
        entity.HasOne<PhotoMetadata>().WithMany().HasForeignKey(x => x.PhotoId).OnDelete(DeleteBehavior.Restrict);
    }
}
