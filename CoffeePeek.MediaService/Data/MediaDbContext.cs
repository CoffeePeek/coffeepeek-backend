using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.MediaService.Data;

public class MediaDbContext(DbContextOptions<MediaDbContext> options) : DbContext(options)
{
    public DbSet<PhotoMetadata> Photos => Set<PhotoMetadata>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PhotoMetadata>(entity =>
        {
            entity.ToTable("Photos", table => table.HasCheckConstraint(
                "CK_Photos_ShopAttachmentPair",
                "(\"ShopAttachmentId\" IS NULL) = (\"ShopAttachmentKind\" IS NULL)"));
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).ValueGeneratedOnAdd();

            entity.Property(p => p.StorageKey).IsRequired().HasMaxLength(255);
            entity.HasIndex(p => p.StorageKey).IsUnique();

            entity.Property(p => p.FileName).IsRequired().HasMaxLength(255);
            entity.Property(p => p.ContentType).IsRequired().HasMaxLength(100);
            entity.Property(p => p.SizeBytes).IsRequired();
            entity.Property(p => p.BucketType).IsRequired().HasConversion<string>().HasMaxLength(20);
            entity.Property(p => p.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
            entity.Property(p => p.OwnerType).IsRequired().HasConversion<string>().HasMaxLength(20);
            entity.Property(p => p.OwnerId).IsRequired();
            entity.Property(p => p.CheckInId);
            entity.Property(p => p.ShopAttachmentId);
            entity.Property(p => p.ShopAttachmentKind).HasConversion<string>().HasMaxLength(20);
            entity.Property(p => p.ClaimedAtUtc);
            entity.Property(p => p.OriginalStorageKey).HasMaxLength(255);
            entity.Property(p => p.UploadedAt).IsRequired();
            entity.Property(p => p.PermalinkExpiresAt);
            entity.Property(p => p.ScheduledDeletionAt);
            entity.Property(p => p.DeletedAt);

            entity.HasIndex(p => new { p.OwnerType, p.OwnerId });
            entity.HasIndex(p => p.Status);
            entity.HasIndex(p => p.CheckInId);
            entity.HasIndex(p => new { p.ShopAttachmentId, p.ShopAttachmentKind });
            entity.HasIndex(p => new { p.Status, p.ScheduledDeletionAt });
        });

        base.OnModelCreating(modelBuilder);
    }
}
