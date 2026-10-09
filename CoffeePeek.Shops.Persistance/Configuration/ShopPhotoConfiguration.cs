using CoffeePeek.Shops.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public class ShopPhotoConfiguration : IEntityTypeConfiguration<ShopPhoto>
{
    public void Configure(EntityTypeBuilder<ShopPhoto> builder)
    {
        // Keep the previous binary's nullable FK and data through the compatibility window.
        builder.HasOne<Domain.Aggregates.CheckInAggregate.CheckIn>().WithMany()
            .HasForeignKey("CanonicalCheckInId").IsRequired(false);
        builder.Property(p => p.SortIndex)
            .IsRequired()
            .HasDefaultValue(0);

        // CoffeeShopId is a shadow FK on ShopPhotos
        builder.HasIndex("CoffeeShopId", nameof(ShopPhoto.SortIndex))
            .HasDatabaseName("IX_ShopPhotos_CoffeeShopId_SortIndex");
    }
}
