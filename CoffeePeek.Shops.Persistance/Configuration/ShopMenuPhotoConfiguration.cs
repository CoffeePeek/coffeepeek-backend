using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public class ShopMenuPhotoConfiguration : IEntityTypeConfiguration<ShopMenuPhoto>
{
    public void Configure(EntityTypeBuilder<ShopMenuPhoto> builder)
    {
        builder.ToTable("ShopMenuPhotos");
        builder.HasKey(p => p.Id);
        // The id is supplied by Media for retry-safe attachment. Without this EF treats
        // a new photo added to an already tracked menu as an existing row to UPDATE.
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.FileName)
            .IsRequired()
            .HasMaxLength(BusinessConstants.MaxMenuPhotoFileNameLength);

        builder.Property(p => p.ContentType)
            .IsRequired()
            .HasMaxLength(BusinessConstants.MaxMenuPhotoContentTypeLength);

        builder.Property(p => p.StorageKey)
            .IsRequired()
            .HasMaxLength(BusinessConstants.MaxMenuPhotoStorageKeyLength);
    }
}
