using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.MediaService.Data;

public sealed class PhotoBackfillRunConfiguration : IEntityTypeConfiguration<PhotoBackfillRun>
{
    public void Configure(EntityTypeBuilder<PhotoBackfillRun> entity)
    {
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).ValueGeneratedNever();
        entity.Property(x => x.Phase).HasConversion<string>().HasMaxLength(30);
        // There is one archive producer and one global rate budget, even across replicas.
        entity.HasIndex(x => x.Paused).IsUnique().HasFilter("\"Paused\" = FALSE");
        entity.ToTable("PhotoBackfillRuns", table =>
        {
            table.HasCheckConstraint("CK_PhotoBackfillRuns_Bounds", "\"BatchSize\" BETWEEN 1 AND 100 AND \"MinBatchIntervalSeconds\" BETWEEN 5 AND 3600 AND \"RecentSinceUtc\" < \"BeforeUtc\"");
            table.HasCheckConstraint("CK_PhotoBackfillRuns_Cursor", "(\"CursorSortAtUtc\" IS NULL AND \"CursorCheckInId\" IS NULL AND \"CursorPhotoId\" IS NULL) OR (\"CursorSortAtUtc\" IS NOT NULL AND \"CursorCheckInId\" IS NOT NULL AND \"CursorPhotoId\" IS NOT NULL)");
        });
    }
}
