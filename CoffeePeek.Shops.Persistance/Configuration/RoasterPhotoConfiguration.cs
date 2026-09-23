using CoffeePeek.Shops.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public class RoasterPhotoConfiguration : IEntityTypeConfiguration<RoasterPhoto>
{
    public void Configure(EntityTypeBuilder<RoasterPhoto> builder)
    {
        // Id is assigned client-side in the domain constructor, so EF must not treat a
        // set key as "already persisted" — otherwise ReplacePhotos on a tracked roaster
        // marks new photos Modified instead of Added and the UPDATE hits 0 rows (409).
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.SortIndex)
            .IsRequired()
            .HasDefaultValue(0);

        // RoasterId is a shadow FK on RoasterPhotos
        builder.HasIndex("RoasterId", nameof(RoasterPhoto.SortIndex))
            .HasDatabaseName("IX_RoasterPhotos_RoasterId_SortIndex");
    }
}
