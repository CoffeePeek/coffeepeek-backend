using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public class CoffeeShopTagConfiguration : IEntityTypeConfiguration<CoffeeShopTag>
{
    public void Configure(EntityTypeBuilder<CoffeeShopTag> builder)
    {
        builder.ToTable("CoffeeShopTags");
        builder.HasKey(t => new { t.ShopId, t.TagId });

        builder.Property(t => t.AssignedByUserId);
        builder.Property(t => t.AssignedAtUtc).IsRequired();

        builder.HasOne(t => t.Tag)
            .WithMany()
            .HasForeignKey(t => t.TagId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CoffeeShop>()
            .WithMany(s => s.ShopTags)
            .HasForeignKey(t => t.ShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.TagId);
    }
}
