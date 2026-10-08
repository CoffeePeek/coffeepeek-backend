using CoffeePeek.Shops.Domain;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CoffeePeek.Shops.Persistance.Configuration;

public class ShopMenuConfiguration : IEntityTypeConfiguration<ShopMenu>
{
    public void Configure(EntityTypeBuilder<ShopMenu> builder)
    {
        builder.ToTable("ShopMenus");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Currency)
            .IsRequired()
            .HasMaxLength(BusinessConstants.MaxMenuCurrencyLength);

        builder.Property(m => m.ParseError)
            .HasMaxLength(BusinessConstants.MaxMenuParseErrorLength);

        builder.HasIndex(m => m.CoffeeShopId).IsUnique();

        builder.HasOne<CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShop>()
            .WithMany()
            .HasForeignKey(m => m.CoffeeShopId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.Items)
            .WithOne()
            .HasForeignKey(i => i.ShopMenuId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.Photos)
            .WithOne()
            .HasForeignKey(p => p.ShopMenuId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(m => m.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(m => m.Photos).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
