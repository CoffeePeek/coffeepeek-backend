using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Persistance.Community;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public sealed class CheckInPhotoVariantManifestConfiguration : IEntityTypeConfiguration<CheckInPhotoVariantManifest>
{
    public void Configure(EntityTypeBuilder<CheckInPhotoVariantManifest> builder)
    {
        builder.ToTable("CheckInPhotoVariantManifests");
        builder.HasKey(x => new { x.PhotoId, x.SourceVersion, x.RecipeVersion });
        builder.Property(x => x.SourceVersion).HasMaxLength(64);
        builder.Property(x => x.VariantsJson).HasColumnType("jsonb");
        builder.HasOne<CheckInPhoto>().WithMany().HasForeignKey(x => x.PhotoId).OnDelete(DeleteBehavior.Cascade);
    }
}
